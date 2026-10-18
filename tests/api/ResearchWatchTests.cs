using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class ResearchWatchTests
{
    private sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public AppDbContext Db { get; }
        public Store()
        {
            Connection.Open();
            Db = NewContext();
            Db.Database.EnsureCreated();
        }
        public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
        public IngestionService Service(IRemoteFetcher fetcher, AppDbContext? context = null) => new(context ?? Db,
            fetcher, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAlex:ApiKey"] = "test-key" }).Build(),
            NullLogger<IngestionService>.Instance);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    private sealed class Fetcher(Func<RemoteRequest, RemoteResponse> fetch) : IRemoteFetcher
    {
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken) => Task.FromResult(fetch(request));
    }

    private static Source Source() => new() { Name = "TEST FIXTURE watch", Kind = "openalex", Query = "consciousness", Enabled = false, ResultLimit = 2, IntervalMinutes = 240 };
    private static RemoteResponse Page(RemoteRequest request, string? nextCursor, params int[] ids) => new(200, request.Url, "application/json",
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            meta = new { next_cursor = nextCursor },
            results = ids.Select(id => new { id = "https://openalex.org/W" + id, display_name = "TEST FIXTURE paper " + id, publication_date = "2026-10-15" })
        })), null, null);

    [Fact]
    public async Task InitialRefreshSeedsNewestPageAndStartsCheckpointWithoutHistoricalBacklog()
    {
        await using var store = new Store();
        var source = Source();
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        var before = DateTimeOffset.UtcNow;
        var calls = 0;
        var fetcher = new Fetcher(request =>
        {
            calls++;
            var query = QueryHelpers.ParseQuery(request.Url.Query);
            Assert.Equal("publication_date:desc", query["sort"].ToString());
            Assert.Equal("*", query["cursor"].ToString());
            Assert.StartsWith("from_publication_date:", query["filter"].ToString());
            Assert.Equal("consciousness", query["search.title_and_abstract"].ToString());
            Assert.False(query.ContainsKey("search"));
            var from = DateTimeOffset.Parse(query["filter"].ToString().Split(',')[0].Split(':')[1]);
            Assert.InRange(from, before.AddYears(-2).AddDays(-1), before.AddYears(-2).AddDays(1));
            return Page(request, "historical-backlog", 100);
        });
        var job = await store.Service(fetcher).RefreshAsync(source, default);
        Assert.Equal("completed", job.Status);
        Assert.Equal(1, calls);
        Assert.InRange(source.ResearchCheckpointAt!.Value, before, DateTimeOffset.UtcNow);
        Assert.Null(source.ResearchCursor);
        Assert.Null(source.ResearchFromDate);
        Assert.Null(source.ResearchWindowStartedAt);
        Assert.Single(await store.Db.Items.ToListAsync());
        Assert.True(source.NextSyncAt > DateTimeOffset.UtcNow.AddMinutes(200));
    }

    [Fact]
    public async Task KeylessWatchSamplesFirstPageAndRecoversPendingCursorWithoutClaimingFullCoverage()
    {
        await using var store = new Store();
        var source = Source();
        source.ResearchCheckpointAt = DateTimeOffset.UtcNow.AddDays(-3);
        source.ResearchCursor = "requires-authentication";
        source.ResearchFromDate = DateTimeOffset.UtcNow.AddDays(-20);
        source.ResearchWindowStartedAt = DateTimeOffset.UtcNow.AddDays(-2);
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        var calls = 0;
        var fetcher = new Fetcher(request =>
        {
            calls++;
            var query = QueryHelpers.ParseQuery(request.Url.Query);
            Assert.Equal("*", query["cursor"].ToString());
            Assert.False(query.ContainsKey("api_key"));
            return Page(request, "requires-authentication", 100, 101);
        });
        var service = new IngestionService(store.Db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var before = DateTimeOffset.UtcNow;
        var first = await service.RefreshAsync(source, default);
        var repeated = await service.RefreshAsync(source, default);
        Assert.Equal("completed", first.Status);
        Assert.Equal("completed", repeated.Status);
        Assert.Equal(2, calls);
        Assert.Equal(0, repeated.Imported);
        Assert.Equal(2, await store.Db.Items.CountAsync());
        Assert.Null(source.ResearchCursor);
        Assert.Null(source.ResearchFromDate);
        Assert.Null(source.ResearchWindowStartedAt);
        Assert.InRange(source.ResearchCheckpointAt!.Value, before, DateTimeOffset.UtcNow);
        Assert.True(source.NextSyncAt > DateTimeOffset.UtcNow.AddMinutes(200));
    }

    [Fact]
    public async Task IncrementalWatchPersistsFixedWindowAndResumesOnePagePerRefresh()
    {
        await using var store = new Store();
        var checkpoint = new DateTimeOffset(2026, 10, 12, 11, 0, 0, TimeSpan.Zero);
        var source = Source(); source.ResearchCheckpointAt = checkpoint;
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        var calls = 0;
        var fetcher = new Fetcher(request =>
        {
            var query = QueryHelpers.ParseQuery(request.Url.Query);
            Assert.Equal("publication_date:desc", query["sort"].ToString());
            Assert.StartsWith("from_publication_date:2026-10-05,to_publication_date:", query["filter"].ToString());
            calls++;
            Assert.Equal(calls == 1 ? "*" : "next+/=", query["cursor"].ToString());
            return calls == 1 ? Page(request, "next+/=", 100, 101) : Page(request, null, 101, 102);
        });
        await store.Service(fetcher).RefreshAsync(source, default);
        Assert.Equal(1, calls);
        Assert.Equal(checkpoint, source.ResearchCheckpointAt);
        Assert.Equal(checkpoint.AddDays(-7), source.ResearchFromDate);
        Assert.Equal("next+/=", source.ResearchCursor);
        var windowStart = source.ResearchWindowStartedAt;
        Assert.NotNull(windowStart);
        Assert.InRange(source.NextSyncAt, DateTimeOffset.UtcNow.AddSeconds(40), DateTimeOffset.UtcNow.AddSeconds(70));

        // A fresh context represents a restart between pages; continuation is database state.
        await using var resumedDb = store.NewContext();
        var resumed = await resumedDb.Sources.SingleAsync();
        Assert.Equal(windowStart, resumed.ResearchWindowStartedAt);
        await store.Service(fetcher, resumedDb).RefreshAsync(resumed, default);
        Assert.Equal(2, calls);
        Assert.Equal(windowStart, resumed.ResearchCheckpointAt);
        Assert.Null(resumed.ResearchCursor);
        Assert.Null(resumed.ResearchFromDate);
        Assert.Null(resumed.ResearchWindowStartedAt);
        Assert.Equal(3, await resumedDb.Items.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderFailureDoesNotMoveCheckpointOrPendingCursor(bool timeout)
    {
        await using var store = new Store();
        var source = Source();
        source.ResearchCheckpointAt = DateTimeOffset.UtcNow.AddDays(-2);
        source.ResearchFromDate = source.ResearchCheckpointAt.Value.AddDays(-7);
        source.ResearchWindowStartedAt = DateTimeOffset.UtcNow.AddHours(-1);
        source.ResearchCursor = "pending-page";
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        var oldCheckpoint = source.ResearchCheckpointAt;
        var oldFrom = source.ResearchFromDate;
        var oldWindow = source.ResearchWindowStartedAt;
        var fetcher = new Fetcher(_ =>
        {
            if (timeout) throw new OperationCanceledException("Provider deadline");
            throw new RemoteFetchException("Remote returned HTTP 401.");
        });
        var job = await store.Service(fetcher).RefreshAsync(source, default);
        Assert.Equal("failed", job.Status);
        await store.Db.Entry(source).ReloadAsync();
        Assert.Equal(oldCheckpoint, source.ResearchCheckpointAt);
        Assert.Equal(oldFrom, source.ResearchFromDate);
        Assert.Equal(oldWindow, source.ResearchWindowStartedAt);
        Assert.Equal("pending-page", source.ResearchCursor);
        Assert.Empty(await store.Db.Items.ToListAsync());
    }

    [Fact]
    public async Task CallerCancellationDoesNotAdvanceResearchWindow()
    {
        await using var store = new Store();
        using var cancellation = new CancellationTokenSource();
        var source = Source(); source.ResearchCheckpointAt = DateTimeOffset.UtcNow.AddDays(-1);
        var checkpoint = source.ResearchCheckpointAt;
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        var fetcher = new Fetcher(_ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.Service(fetcher).RefreshAsync(source, cancellation.Token));
        await store.Db.Entry(source).ReloadAsync();
        Assert.Equal(checkpoint, source.ResearchCheckpointAt);
        Assert.Null(source.ResearchCursor);
        Assert.Null(source.ResearchFromDate);
        Assert.Null(source.ResearchWindowStartedAt);
    }

    [Fact]
    public async Task FetchOnlyDoesNotMutatePersistedWatchProgressAndInteractiveSearchKeepsRelevance()
    {
        await using var store = new Store();
        var source = Source(); source.ResearchCheckpointAt = DateTimeOffset.UtcNow.AddDays(-1);
        var checkpoint = source.ResearchCheckpointAt;
        var requests = new List<RemoteRequest>();
        var fetcher = new Fetcher(request => { requests.Add(request); return Page(request, "next-page", 100); });
        var service = store.Service(fetcher);
        var batch = await service.FetchSourceAsync(source, default);
        Assert.Equal("next-page", batch.ResearchProgress!.Cursor);
        Assert.Equal(checkpoint, source.ResearchCheckpointAt);
        Assert.Null(source.ResearchCursor);
        Assert.Null(source.ResearchFromDate);
        Assert.Null(source.ResearchWindowStartedAt);
        await service.SearchResearchAsync("openalex", "consciousness", default);
        var interactive = QueryHelpers.ParseQuery(requests[1].Url.Query);
        Assert.False(interactive.ContainsKey("sort"));
        Assert.False(interactive.ContainsKey("cursor"));
        Assert.False(interactive.ContainsKey("filter"));
    }
}
