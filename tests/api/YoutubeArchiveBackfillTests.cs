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

public class YoutubeArchiveBackfillTests
{
    private const string Channel = "UC4sdJIrKWKxymZ5lwVSBzTw";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T12:00:00Z");
    private sealed class Store : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AppDbContext Db { get; }
        public Store()
        {
            connection.Open();
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private sealed class Remote : IRemoteFetcher
    {
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken ct) => throw new Exception("Keyless history must not call the Data API");
    }
    private sealed class Client(Func<int, int, YtDlpPage> route) : IYtDlpClient
    {
        public List<(int Start, int Limit)> Calls { get; } = [];
        public Task<YtDlpPage> FetchAsync(string channel, int start, int limit, CancellationToken ct)
        { Assert.Equal(Channel, channel); ct.ThrowIfCancellationRequested(); Calls.Add((start, limit)); return Task.FromResult(route(start, limit)); }
    }
    private static YtDlpEntry Entry(int index, string? date = null) => new($"ytfixture{index:00}", "TEST FIXTURE old video " + index,
        Channel, "TEST FIXTURE creator", "Supplied description", null, date, "public", false, false, index);
    private static YtDlpPage Page(int start, params YtDlpEntry[] entries) => new(1, "2026.10.26", Channel, start,
        start + entries.Length, true, entries, []);
    private static Source Source() => new() { Kind = "youtube", Name = "TEST FIXTURE channel", Url = "https://www.youtube.com/channel/" + Channel, BackfillLimit = 2 };
    private static IConfiguration Config() => new ConfigurationBuilder().Build();

    [Fact]
    public async Task Keyless_is_default_and_daily_cached_batches_keep_checking_health_and_skip_private_known_aliases()
    {
        await using var store = new Store();
        var source = Source(); store.Db.Add(source); await store.Db.SaveChangesAsync();
        var ingestion = new IngestionService(store.Db, new Remote(), Config(), NullLogger<IngestionService>.Instance);
        var known = YoutubeArchiveBackfill.Parse(Entry(1), Channel, "2026.10.26")!;
        known.CanonicalUrl = "https://www.youtube.com/shorts/" + known.ProviderId;
        var (saved, _) = await ingestion.UpsertAsync(known, source.Id, [], default);
        saved.IsPublic = false; store.Db.Add(new ItemState { ItemId = saved.Id, Saved = true, Read = true }); await store.Db.SaveChangesAsync();
        var client = new Client((start, limit) => limit == 1 ? Page(1, Entry(1)) : Page(start, Entry(1), Entry(2), Entry(3, "20190102"), Entry(4), Entry(5)));
        var engine = new SourceBackfill(store.Db, new Remote(), Config(), client);
        var first = await engine.FetchAsync(source, default, Now);
        Assert.Equal(new[] { "ytfixture02", "ytfixture03" }, first.Items.Select(i => i.ProviderId));
        Assert.Null(first.Items[0].PublishedAt); Assert.Equal(2019, first.Items[1].PublishedAt!.Value.Year);
        source.BackfillStateJson = first.StateJson; await store.Db.SaveChangesAsync();
        // Recreate the adapter as after restart; remaining entries come from durable state.
        var second = await new SourceBackfill(store.Db, new Remote(), Config(), client).FetchAsync(source, default, Now.AddDays(1));
        Assert.Equal(new[] { "ytfixture04", "ytfixture05" }, second.Items.Select(i => i.ProviderId));
        Assert.Equal("caught-up", second.Status);
        Assert.Equal(new[] { (1, 30), (1, 1) }, client.Calls);
        Assert.False(saved.IsPublic); Assert.True((await store.Db.States.SingleAsync()).Saved); Assert.True((await store.Db.States.SingleAsync()).Read);
        Assert.Contains("yt-dlp 2026.10.26", first.Message);
    }

    [Fact]
    public async Task Metadata_failure_is_persisted_without_advancing_archive_or_recent_feed_and_health_check_recovers_without_import()
    {
        await using var store = new Store();
        var source = Source(); source.LastSuccessAt = Now; source.LastImportedCount = 9;
        store.Db.Add(source); await store.Db.SaveChangesAsync();
        var fail = false;
        var client = new Client((start, limit) => fail ? throw new YtDlpException("yt-dlp: YouTube blocked the metadata request. Retry later.", "blocked")
            : limit == 1 ? Page(1, Entry(1)) : Page(start, Entry(1), Entry(2), Entry(3), Entry(4)));
        var ingestion = new IngestionService(store.Db, new Remote(), Config(), NullLogger<IngestionService>.Instance);
        var runner = new SourceBackfillService(store.Db, new SourceBackfill(store.Db, new Remote(), Config(), client), ingestion,
            Config(), NullLogger<SourceBackfillService>.Instance);
        var initial = await runner.RunAsync(source, default, force: true); Assert.Equal(2, initial.Imported);
        var cursor = source.BackfillStateJson;
        fail = true;
        var failed = await runner.RunAsync(source, default, force: true);
        await store.Db.Entry(source).ReloadAsync();
        Assert.Equal("failed", failed.Status); Assert.Equal("failed", source.BackfillStatus); Assert.Contains("yt-dlp", source.BackfillMessage);
        Assert.Equal(cursor, source.BackfillStateJson); Assert.Equal(2, source.BackfillImportedCount); Assert.Equal(2, await store.Db.Items.CountAsync());
        Assert.Equal(Now, source.LastSuccessAt); Assert.Equal(9, source.LastImportedCount); Assert.Null(source.LastError);
        fail = false;
        var health = await runner.RunAsync(source, default, force: true, checkOnly: true);
        Assert.Equal("backfill-check", health.Kind); Assert.Equal("completed", health.Status); Assert.Equal(0, health.Imported);
        Assert.Equal(cursor, source.BackfillStateJson); Assert.Equal(2, source.BackfillImportedCount); Assert.Equal(2, await store.Db.Items.CountAsync());
        Assert.Equal(0, source.BackfillFailureCount); Assert.Contains("health check passed", source.BackfillMessage);
        var resumed = await runner.RunAsync(source, default, force: true); Assert.Equal(2, resumed.Imported);
        Assert.Equal(4, await store.Db.Items.CountAsync());
    }

    [Fact]
    public async Task Empty_archive_rechecks_later_and_api_cursor_switches_cleanly_to_keyless()
    {
        await using var store = new Store();
        var source = Source(); source.BackfillStateJson = JsonSerializer.Serialize(new { Engine = "youtube", Token = "old-private-api-token", Offset = 20 });
        var client = new Client((start, _) => Page(start));
        var engine = new SourceBackfill(store.Db, new Remote(), Config(), client);
        var first = await engine.FetchAsync(source, default, Now);
        Assert.Empty(first.Items); Assert.Equal("caught-up", first.Status); Assert.DoesNotContain("old-private-api-token", first.StateJson);
        source.BackfillStateJson = first.StateJson;
        await engine.FetchAsync(source, default, Now.AddDays(1));
        Assert.Equal(new[] { (1, 30), (1, 30) }, client.Calls);
    }

    [Fact]
    public async Task Deterministic_scan_limit_stops_automatic_retries_and_health_checks_the_blocked_position()
    {
        await using var store = new Store();
        var source = Source();
        source.BackfillStateJson = JsonSerializer.Serialize(new { Engine = "youtube-yt-dlp-v1", ChannelId = Channel, NextStart = 3001, Pending = Array.Empty<YtDlpEntry>() });
        store.Db.Add(source); await store.Db.SaveChangesAsync();
        var blocked = true;
        var client = new Client((start, _) => blocked ? throw new YtDlpException("yt-dlp scan limit; automatic checks paused.", "request-limit") : Page(start));
        var ingestion = new IngestionService(store.Db, new Remote(), Config(), NullLogger<IngestionService>.Instance);
        var runner = new SourceBackfillService(store.Db, new SourceBackfill(store.Db, new Remote(), Config(), client), ingestion, Config(), NullLogger<SourceBackfillService>.Instance);
        Assert.Equal("failed", (await runner.RunAsync(source, default)).Status);
        Assert.Equal("scan-limit", source.BackfillStatus); Assert.Null(source.BackfillNextAt);
        Assert.Equal("skipped", (await runner.RunAsync(source, default)).Status); Assert.Single(client.Calls);
        Assert.Equal("failed", (await runner.RunAsync(source, default, force: true, checkOnly: true)).Status);
        Assert.Equal((3001, 1), client.Calls.Last()); // A latest-page success would be misleading here.
        var cursor = source.BackfillStateJson;
        blocked = false;
        var health = await runner.RunAsync(source, default, force: true, checkOnly: true);
        Assert.Equal("completed", health.Status); Assert.Equal("ready", source.BackfillStatus);
        Assert.Equal(cursor, source.BackfillStateJson); Assert.Empty(await store.Db.Items.ToListAsync());
        Assert.NotNull(source.BackfillNextAt); Assert.Equal(0, source.BackfillFailureCount);
    }

    [Fact]
    public async Task Long_international_metadata_fits_the_durable_cursor_and_resumes()
    {
        await using var store = new Store(); var source = Source(); source.BackfillLimit = 1;
        var entries = Enumerable.Range(1, 30).Select(i => Entry(i) with
        { Title = new string('語', 1000), Description = new string('語', 5000) }).ToArray();
        var client = new Client((start, limit) => limit == 1 ? Page(1, Entry(1)) : Page(start, entries));
        var engine = new SourceBackfill(store.Db, new Remote(), Config(), client);
        var first = await engine.FetchAsync(source, default, Now);
        Assert.True(first.StateJson!.Length < 1_000_000);
        source.BackfillStateJson = first.StateJson;
        Assert.Equal("ytfixture02", Assert.Single((await engine.FetchAsync(source, default, Now.AddDays(1))).Items).ProviderId);
    }

    [Fact]
    public void Only_valid_available_channel_videos_are_imported_and_dates_are_not_estimated()
    {
        var item = Entry(1);
        Assert.Null(YoutubeArchiveBackfill.Parse(item with { UploadDate = "2 years ago" }, Channel, "2026.10.26")!.PublishedAt);
        foreach (var rejected in new[] { item with { Availability = "private" }, item with { Availability = "unlisted" },
            item with { ChannelId = "UCother" }, item with { Id = "invalid" }, item with { IsLive = true }, item with { IsUpcoming = true } })
            Assert.Null(YoutubeArchiveBackfill.Parse(rejected, Channel, "2026.10.26"));
    }
}
