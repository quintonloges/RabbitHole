using Microsoft.AspNetCore.WebUtilities;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class SourceBackfillAdapterTests
{
    private const string Channel = "UC4sdJIrKWKxymZ5lwVSBzTw";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T12:00:00Z");

    private sealed class Remote(Func<RemoteRequest, RemoteResponse> route) : IRemoteFetcher
    {
        public List<Uri> Requests { get; } = [];
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Requests.Add(request.Url); return Task.FromResult(route(request));
        }
    }
    private sealed class Store : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AppDbContext Db { get; private set; } = null!;
        public async Task InitializeAsync()
        {
            await connection.OpenAsync();
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await Db.Database.EnsureCreatedAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private static RemoteResponse Json(RemoteRequest r, object value) => new(200, r.Url, "application/json", JsonSerializer.SerializeToUtf8Bytes(value), null, null);
    private static IConfiguration Config(bool key = true) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["YouTube:BackfillMode"] = "api", ["YouTube:ApiKey"] = key ? "TEST-FIXTURE-key" : null }).Build();
    private static object Video(string id, string? published = "2020-01-01T00:00:00Z", string privacy = "public", string? owner = Channel) => new
    {
        snippet = new
        {
            title = "TEST FIXTURE " + id,
            description = "A source-supplied description",
            channelId = Channel,
            videoOwnerChannelId = owner,
            videoOwnerChannelTitle = "TEST FIXTURE creator",
            publishedAt = "2026-11-10T00:00:00Z"
        },
        contentDetails = new { videoId = id, videoPublishedAt = published },
        status = new { privacyStatus = privacy }
    };
    private static Source Source() => new() { Kind = "youtube", Url = "https://www.youtube.com/channel/" + Channel, BackfillLimit = 2, Name = "TEST FIXTURE channel" };

    private sealed class DiagnosticClient(bool available) : IYtDlpClient
    {
        public int Calls { get; private set; }
        public Task<YtDlpPage> FetchAsync(string channelId, int start, int limit, CancellationToken ct)
        {
            Calls++;
            if (!available) throw new YtDlpException("TEST FIXTURE missing yt-dlp", "dependency-missing");
            return Task.FromResult(new YtDlpPage(1, "2026.10.26", channelId, start, start, true, [], []));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Youtube_api_health_check_uses_configured_adapter_without_importing_or_advancing(bool hasCursor)
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = Source();
        source.LastSuccessAt = Now; source.LastImportedCount = 5;
        source.BackfillFailureCount = 2; source.BackfillStatus = "failed";
        source.BackfillNextAt = DateTimeOffset.UtcNow.AddDays(3);
        source.BackfillStateJson = hasCursor ? JsonSerializer.Serialize(new
        { Engine = "youtube", Playlist = "UUfixtureUploadsArchive", Token = "TEST-FIXTURE-page", Offset = 2, Through = Now.AddMonths(-1) }) : null;
        var cursor = source.BackfillStateJson;
        store.Db.Add(source); await store.Db.SaveChangesAsync();
        var remote = new Remote(r => r.Url.AbsolutePath.EndsWith("/channels")
            ? Json(r, new { items = new[] { new { id = Channel, contentDetails = new { relatedPlaylists = new { uploads = "UUfixtureUploadsArchive" } } } } })
            : Json(r, new { nextPageToken = "TEST-FIXTURE-next", items = new[] { Video("fixture0001"), Video("fixture0002"), Video("fixture0003") } }));
        var config = Config(); var ytDlp = new DiagnosticClient(false);
        var runner = new SourceBackfillService(store.Db, new SourceBackfill(store.Db, remote, config, ytDlp),
            new IngestionService(store.Db, remote, config, NullLogger<IngestionService>.Instance), config, NullLogger<SourceBackfillService>.Instance);
        var before = DateTimeOffset.UtcNow;
        var job = await runner.RunAsync(source, default, force: true, checkOnly: true);
        Assert.Equal("completed", job.Status); Assert.Equal("backfill-check", job.Kind); Assert.Equal(0, job.Imported);
        Assert.Equal(0, ytDlp.Calls);
        Assert.Equal(hasCursor ? 1 : 2, remote.Requests.Count);
        var query = QueryHelpers.ParseQuery(remote.Requests.Last().Query);
        if (hasCursor) Assert.Equal("TEST-FIXTURE-page", query["pageToken"].ToString());
        else Assert.False(query.ContainsKey("pageToken"));
        Assert.Equal("UUfixtureUploadsArchive", query["playlistId"].ToString());
        Assert.Equal("TEST-FIXTURE-key", query["key"].ToString());
        await store.Db.Entry(source).ReloadAsync();
        Assert.Equal(cursor, source.BackfillStateJson); Assert.Equal(0, source.BackfillImportedCount);
        Assert.Empty(await store.Db.Items.ToListAsync());
        Assert.Equal(0, source.BackfillFailureCount); Assert.Equal("ready", source.BackfillStatus);
        Assert.Contains("YouTube Data API", source.BackfillMessage);
        Assert.True(source.BackfillLastSuccessAt >= before);
        Assert.True(source.BackfillNextAt <= DateTimeOffset.UtcNow);
        Assert.Equal(Now, source.LastSuccessAt); Assert.Equal(5, source.LastImportedCount); Assert.Null(source.LastError);
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("forbidden")]
    [InlineData("expired-token")]
    [InlineData("invalid-mode")]
    public async Task Youtube_api_health_failure_is_not_hidden_by_working_ytdlp(string failure)
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = Source(); source.LastSuccessAt = Now; source.BackfillLastSuccessAt = Now;
        source.BackfillStateJson = JsonSerializer.Serialize(new
        { Engine = "youtube", Playlist = "UUfixtureUploadsArchive", Token = "TEST-FIXTURE-page", Offset = 2, Through = Now.AddMonths(-1) });
        var cursor = source.BackfillStateJson;
        store.Db.Add(source); await store.Db.SaveChangesAsync();
        var config = failure == "invalid-mode" ? new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["YouTube:BackfillMode"] = "invalid" }).Build() : Config(failure != "missing-key");
        var remote = new Remote(_ => throw new RemoteFetchException(failure == "expired-token"
            ? "Remote source returned HTTP 400." : "Remote source returned HTTP 403."));
        var ytDlp = new DiagnosticClient(true);
        var runner = new SourceBackfillService(store.Db, new SourceBackfill(store.Db, remote, config, ytDlp),
            new IngestionService(store.Db, remote, config, NullLogger<IngestionService>.Instance), config, NullLogger<SourceBackfillService>.Instance);
        var job = await runner.RunAsync(source, default, force: true, checkOnly: true);
        Assert.Equal("failed", job.Status); Assert.Equal(0, job.Imported); Assert.Equal(0, ytDlp.Calls);
        Assert.Equal(failure is "missing-key" or "invalid-mode" ? 0 : 1, remote.Requests.Count);
        await store.Db.Entry(source).ReloadAsync();
        Assert.Equal(cursor, source.BackfillStateJson); Assert.Empty(await store.Db.Items.ToListAsync());
        Assert.Equal("failed", source.BackfillStatus); Assert.Equal(1, source.BackfillFailureCount);
        Assert.Equal(Now, source.BackfillLastSuccessAt); Assert.Equal(Now, source.LastSuccessAt);
        Assert.True(source.BackfillNextAt > DateTimeOffset.UtcNow);
        if (failure == "missing-key") Assert.Contains("YOUTUBE_API_KEY", source.BackfillMessage);
    }

    [Fact]
    public async Task Youtube_missing_key_makes_no_outbound_request_and_does_not_claim_history_was_imported()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var remote = new Remote(_ => throw new Exception("Unexpected request"));
        var configuration = Config(false);
        var ingestion = new IngestionService(store.Db, remote, configuration, NullLogger<IngestionService>.Instance);
        var batch = await new SourceBackfill(store.Db, remote, configuration).FetchAsync(Source(), default, Now);
        Assert.Equal("needs-key", batch.Status); Assert.Empty(batch.Items); Assert.Empty(remote.Requests); Assert.Null(batch.StateJson);
    }

    [Fact]
    public async Task Youtube_daily_page_offsets_preserve_remaining_uploads_skip_aliases_and_keep_real_dates()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = Source(); store.Db.Add(source); await store.Db.SaveChangesAsync();
        var remote = new Remote(r => r.Url.AbsolutePath.EndsWith("/channels")
            ? Json(r, new { items = new[] { new { id = Channel, contentDetails = new { relatedPlaylists = new { uploads = "UUfixtureUploadsArchive" } } } } })
            : r.Url.Query.Contains("pageToken=next-page")
                ? Json(r, new { items = new[] { Video("fixture0005") } })
                : Json(r, new
                {
                    nextPageToken = "next-page",
                    items = new[] {
                    Video("fixture0000"), Video("fixture0001"), Video("fixture0002", published: null), Video("fixture0003", privacy: "private"), Video("fixture0004") }
                }));
        var configuration = Config(); var ingestion = new IngestionService(store.Db, remote, configuration, NullLogger<IngestionService>.Instance);
        var known = new NormalizedItem { Provider = "youtube", ProviderId = "fixture0000", CanonicalUrl = "https://www.youtube.com/shorts/fixture0000", Title = "TEST FIXTURE user's original title", Kind = "video" };
        var (existing, _) = await ingestion.UpsertAsync(known, source.Id, [], default);
        existing.IsPublic = false; store.Db.Add(new ItemState { ItemId = existing.Id, Saved = true, Read = true }); await store.Db.SaveChangesAsync();
        var engine = new SourceBackfill(store.Db, remote, configuration);
        var first = await engine.FetchAsync(source, default, Now);
        Assert.Equal(new[] { "fixture0001", "fixture0002" }, first.Items.Select(i => i.ProviderId));
        Assert.Null(first.Items[1].PublishedAt); // Playlist-added date must not fill missing video date.
        foreach (var item in first.Items) await ingestion.UpsertAsync(item, source.Id, [], default);
        source.BackfillStateJson = first.StateJson; await store.Db.SaveChangesAsync();
        var second = await engine.FetchAsync(source, default, Now.AddDays(1));
        Assert.Equal("fixture0004", Assert.Single(second.Items).ProviderId);
        source.BackfillStateJson = second.StateJson; await store.Db.SaveChangesAsync();
        var third = await engine.FetchAsync(source, default, Now.AddDays(2));
        Assert.Equal("fixture0005", Assert.Single(third.Items).ProviderId); Assert.Equal("caught-up", third.Status);
        Assert.Equal(1, remote.Requests.Count(u => u.AbsolutePath.EndsWith("/channels")));
        Assert.Equal("TEST FIXTURE user's original title", existing.Title); Assert.False(existing.IsPublic);
        Assert.True((await store.Db.States.SingleAsync()).Read);
        Assert.DoesNotContain("TEST-FIXTURE-key", first.StateJson);
        Assert.All(first.Items, i => Assert.DoesNotContain("TEST-FIXTURE-key", i.MetadataJson));
    }

    [Fact]
    public void Youtube_only_accepts_public_uploads_from_selected_channel_and_preserves_unknown_date()
    {
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(new[] {
            Video("fixture0001", null), Video("fixture0002", privacy: "unlisted"), Video("fixture0003", owner: "UCother"), Video("invalid") }));
        var rows = data.RootElement.EnumerateArray().Select(e => SourceBackfill.ParseYoutubeUpload(e, Channel)).ToList();
        Assert.NotNull(rows[0]); Assert.Null(rows[0]!.PublishedAt);
        Assert.Equal("https://www.youtube-nocookie.com/embed/fixture0001", rows[0]!.EmbedUrl);
        Assert.All(rows.Skip(1), Assert.Null);
        using var malformed = JsonDocument.Parse("[null,7,{}, {\"snippet\":null,\"contentDetails\":{}}]");
        Assert.All(malformed.RootElement.EnumerateArray(), item => Assert.Null(SourceBackfill.ParseYoutubeUpload(item, Channel)));
    }

    [Fact]
    public async Task Youtube_expired_token_is_reset_without_saving_provider_error_or_secret()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = Source(); source.BackfillStateJson = JsonSerializer.Serialize(new { Engine = "youtube", Playlist = "UUfixtureUploadsArchive", Token = "expired", Offset = 4, Through = Now.AddMonths(-1) });
        var remote = new Remote(_ => throw new RemoteFetchException("Remote source returned HTTP 400."));
        var configuration = Config(); var ingestion = new IngestionService(store.Db, remote, configuration, NullLogger<IngestionService>.Instance);
        var batch = await new SourceBackfill(store.Db, remote, configuration).FetchAsync(source, default, Now);
        Assert.Empty(batch.Items); Assert.Equal("ready", batch.Status);
        using var state = JsonDocument.Parse(batch.StateJson!);
        Assert.Equal(JsonValueKind.Null, state.RootElement.GetProperty("Token").ValueKind); Assert.Equal(0, state.RootElement.GetProperty("Offset").GetInt32());
    }

    [Fact]
    public async Task Arxiv_archive_freezes_date_window_and_advances_independently_of_recent_watch()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = new Source { Kind = "arxiv", Query = "ti:consciousness OR cat:cs.AI", BackfillLimit = 1, ResearchCursor = "unchanged" };
        var remote = new Remote(r =>
        {
            var old = r.Url.Query.Contains("start=1");
            var id = old ? "2301.00002" : "2301.00001";
            var body = $"<feed xmlns='http://www.w3.org/2005/Atom' xmlns:op='http://a9.com/-/spec/opensearch/1.1/'><op:totalResults>2</op:totalResults><entry><id>https://arxiv.org/abs/{id}</id><title>TEST FIXTURE archive</title><link href='https://arxiv.org/abs/{id}'/><published>2023-01-01T00:00:00Z</published></entry></feed>";
            return new RemoteResponse(200, r.Url, "application/atom+xml", Encoding.UTF8.GetBytes(body), null, null);
        });
        var configuration = Config(false); var ingestion = new IngestionService(store.Db, remote, configuration, NullLogger<IngestionService>.Instance);
        var engine = new SourceBackfill(store.Db, remote, configuration);
        var first = await engine.FetchAsync(source, default, Now); source.BackfillStateJson = first.StateJson;
        var second = await engine.FetchAsync(source, default, Now.AddDays(1));
        Assert.Equal("2301.00001", Assert.Single(first.Items).ArxivId); Assert.Equal("2301.00002", Assert.Single(second.Items).ArxivId);
        Assert.Equal("caught-up", second.Status); Assert.Equal("unchanged", source.ResearchCursor);
        Assert.All(remote.Requests, url => Assert.Contains("(ti:consciousness OR cat:cs.AI) AND submittedDate:[199101010000 TO 202610121200]", Uri.UnescapeDataString(url.Query)));
    }

    private const string OpenAlexKey = "TEST-FIXTURE-OPENALEX-PRIVATE-key";
    private static IConfiguration OpenAlexConfig(bool key = true) => new ConfigurationBuilder().AddInMemoryCollection(key
        ? new Dictionary<string, string?> { ["OpenAlex:ApiKey"] = OpenAlexKey } : []).Build();
    private static Source AcademicSource(int limit = 2) => new()
    {
        Name = "TEST FIXTURE papers",
        Kind = "openalex",
        Query = "title:consciousness",
        BackfillLimit = limit,
        ResearchCursor = "TEST FIXTURE fresh cursor",
        ResearchCheckpointAt = Now.AddDays(-1),
        ETag = "TEST FIXTURE etag",
        NextSyncAt = Now.AddHours(3),
        LastError = "TEST FIXTURE fresh error",
        FailureCount = 2
    };
    private static object Paper(int id, string? published = "2005-10-15") => new
    {
        id = "https://openalex.org/W" + id,
        display_name = "TEST FIXTURE paper " + id,
        publication_date = published,
        doi = "https://doi.org/10.1234/fixture" + id
    };
    private static RemoteResponse PaperPage(RemoteRequest request, string? cursor, params object[] papers) =>
        Json(request, new { meta = new { next_cursor = cursor }, results = papers });

    [Fact]
    public async Task Openalex_missing_key_preserves_private_progress_without_contacting_provider()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = AcademicSource(); source.BackfillStateJson = "TEST FIXTURE retained cursor";
        var remote = new Remote(_ => throw new InvalidOperationException("Must not fetch"));
        var config = OpenAlexConfig(false);
        var ingestion = new IngestionService(store.Db, remote, config, NullLogger<IngestionService>.Instance);
        var batch = await new SourceBackfill(store.Db, remote, config).FetchAsync(source, default, Now);
        Assert.Equal("needs-key", batch.Status); Assert.Empty(batch.Items); Assert.Empty(remote.Requests);
        Assert.Equal(source.BackfillStateJson, batch.StateJson); Assert.Equal("TEST FIXTURE fresh cursor", source.ResearchCursor);
    }

    [Fact]
    public async Task Openalex_history_uses_frozen_cutoff_and_independent_cursor_without_persisting_keys()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = AcademicSource(); var requests = 0;
        var remote = new Remote(r => ++requests == 1 ? PaperPage(r, "next+/=", Paper(100), Paper(101)) : PaperPage(r, null, Paper(102)));
        var config = OpenAlexConfig(); var ingestion = new IngestionService(store.Db, remote, config, NullLogger<IngestionService>.Instance);
        var engine = new SourceBackfill(store.Db, remote, config);
        var first = await engine.FetchAsync(source, default, Now);
        Assert.Equal(2, first.Items.Count); Assert.Null(source.BackfillStateJson); // Adapter itself is read-only.
        Assert.DoesNotContain(OpenAlexKey, first.StateJson!);
        Assert.All(first.Items, item => Assert.DoesNotContain(OpenAlexKey, JsonSerializer.Serialize(item)));
        source.BackfillStateJson = first.StateJson;
        var second = await engine.FetchAsync(source, default, Now.AddDays(5));
        Assert.Single(second.Items); Assert.Equal("caught-up", second.Status);
        Assert.Equal("next+/=", QueryHelpers.ParseQuery(remote.Requests[1].Query)["cursor"].ToString());
        Assert.Equal("*", QueryHelpers.ParseQuery(remote.Requests[0].Query)["cursor"].ToString());
        Assert.All(remote.Requests, url =>
        {
            var query = QueryHelpers.ParseQuery(url.Query);
            Assert.Equal("consciousness", query["search.title"].ToString());
            Assert.Equal("2", query["per_page"].ToString());
            Assert.Equal("publication_date:desc", query["sort"].ToString());
            Assert.Equal("to_publication_date:2026-10-12", query["filter"].ToString());
            Assert.Equal(OpenAlexKey, query["api_key"].ToString());
        });
        Assert.Equal("TEST FIXTURE fresh cursor", source.ResearchCursor);
        Assert.Equal(Now.AddDays(-1), source.ResearchCheckpointAt);
        Assert.Equal("TEST FIXTURE etag", source.ETag); Assert.Equal(Now.AddHours(3), source.NextSyncAt);
        Assert.Equal("TEST FIXTURE fresh error", source.LastError); Assert.Equal(2, source.FailureCount);
        using var terminal = JsonDocument.Parse(second.StateJson!);
        Assert.Equal(JsonValueKind.Null, terminal.RootElement.GetProperty("Token").ValueKind);
        Assert.Equal(JsonValueKind.Null, terminal.RootElement.GetProperty("Through").ValueKind);
    }

    [Fact]
    public async Task Openalex_skips_hidden_saved_and_merged_identities_without_changing_user_state()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = AcademicSource(3); store.Db.Add(source); await store.Db.SaveChangesAsync();
        var remote = new Remote(r => PaperPage(r, "next", Paper(100), Paper(101), Paper(102)));
        var config = OpenAlexConfig(); var ingestion = new IngestionService(store.Db, remote, config, NullLogger<IngestionService>.Instance);
        var (existing, _) = await ingestion.UpsertAsync(new NormalizedItem
        {
            Title = "TEST FIXTURE original title",
            Provider = "openalex",
            ProviderId = "W100",
            CanonicalUrl = "https://doi.org/10.1234/fixture100",
            Doi = "10.1234/fixture100"
        }, null, [], default);
        existing.IsPublic = false;
        store.Db.States.Add(new ItemState { ItemId = existing.Id, Saved = true, Read = true });
        store.Db.Items.Add(new ContentItem
        {
            Title = "TEST FIXTURE merged alias",
            Provider = "openalex",
            ProviderId = "W101",
            CanonicalUrl = "https://example.org/old-alias",
            MergedIntoId = existing.Id
        });
        await store.Db.SaveChangesAsync();
        var batch = await new SourceBackfill(store.Db, remote, config).FetchAsync(source, default, Now);
        Assert.Equal("W102", Assert.Single(batch.Items).ProviderId);
        Assert.False(existing.IsPublic); Assert.Equal("TEST FIXTURE original title", existing.Title);
        var state = await store.Db.States.SingleAsync(); Assert.True(state.Read); Assert.True(state.Saved);
        Assert.Equal(2, await store.Db.Items.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Openalex_preserves_unknown_dates_and_filters_out_new_items_returned_outside_window()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = AcademicSource(); var remote = new Remote(r => PaperPage(r, null, Paper(100, null), Paper(101, "2026-11-10")));
        var config = OpenAlexConfig(); var ingestion = new IngestionService(store.Db, remote, config, NullLogger<IngestionService>.Instance);
        var batch = await new SourceBackfill(store.Db, remote, config).FetchAsync(source, default, Now);
        Assert.Null(Assert.Single(batch.Items).PublishedAt); Assert.Equal("W100", batch.Items[0].ProviderId);
        Assert.Equal("caught-up", batch.Status);
    }

    [Fact]
    public async Task Openalex_expired_token_resets_archive_only_without_persisting_error_or_key()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var source = AcademicSource(); source.BackfillStateJson = JsonSerializer.Serialize(new { Engine = "openalex", Token = "expired", Through = Now.AddDays(-30) });
        var prior = source.BackfillStateJson;
        var remote = new Remote(_ => throw new RemoteFetchException("Remote source returned HTTP 400. " + OpenAlexKey));
        var config = OpenAlexConfig(); var ingestion = new IngestionService(store.Db, remote, config, NullLogger<IngestionService>.Instance);
        var batch = await new SourceBackfill(store.Db, remote, config).FetchAsync(source, default, Now);
        Assert.Equal("ready", batch.Status); Assert.Empty(batch.Items);
        Assert.Equal(prior, source.BackfillStateJson); Assert.Equal("TEST FIXTURE fresh cursor", source.ResearchCursor);
        Assert.DoesNotContain(OpenAlexKey, JsonSerializer.Serialize(batch));
        using var state = JsonDocument.Parse(batch.StateJson!);
        Assert.Equal(JsonValueKind.Null, state.RootElement.GetProperty("Token").ValueKind);
    }

    [Fact]
    public async Task Openalex_rejects_oversized_pages_without_advancing_progress()
    {
        await using var store = new Store(); await store.InitializeAsync(); var source = AcademicSource(1);
        var remote = new Remote(r => PaperPage(r, "next", Paper(100), Paper(101)));
        var config = OpenAlexConfig(); var ingestion = new IngestionService(store.Db, remote, config, NullLogger<IngestionService>.Instance);
        await Assert.ThrowsAsync<FormatException>(() => new SourceBackfill(store.Db, remote, config).FetchAsync(source, default, Now));
        Assert.Null(source.BackfillStateJson); Assert.Empty(await store.Db.Items.ToListAsync());
    }

    [Fact]
    public async Task Academic_cursor_validation_blocks_invalid_dates_and_unbounded_tokens_before_fetching()
    {
        await using var store = new Store(); await store.InitializeAsync(); var source = AcademicSource();
        var remote = new Remote(_ => throw new InvalidOperationException("Must not fetch"));
        var config = OpenAlexConfig(); var ingestion = new IngestionService(store.Db, remote, config, NullLogger<IngestionService>.Instance);
        var engine = new SourceBackfill(store.Db, remote, config);
        source.BackfillStateJson = JsonSerializer.Serialize(new { Engine = "openalex", Token = "token", Through = Now.AddDays(1) });
        await Assert.ThrowsAsync<FormatException>(() => engine.FetchAsync(source, default, Now));
        source.BackfillStateJson = JsonSerializer.Serialize(new { Engine = "openalex", Token = new string('x', 2049), Through = Now.AddDays(-30) });
        await Assert.ThrowsAsync<FormatException>(() => engine.FetchAsync(source, default, Now));
        Assert.Empty(remote.Requests);
    }
}
