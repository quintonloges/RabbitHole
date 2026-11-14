using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class SourceBackfillServiceTests
{
    private sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public AppDbContext Db { get; }
        public Store()
        {
            Connection.Open(); Db = NewContext(); Db.Database.EnsureCreated();
        }
        public AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options);
        public SourceBackfillService Service(IRemoteFetcher fetcher, AppDbContext? db = null, bool paused = false, string? key = "TEST FIXTURE key")
        {
            var context = db ?? Db;
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Ingestion:Paused"] = paused.ToString(), ["OpenAlex:ApiKey"] = key }).Build();
            var ingestion = new IngestionService(context, fetcher, config, NullLogger<IngestionService>.Instance);
            return new(context, new SourceBackfill(context, fetcher, config), ingestion, config, NullLogger<SourceBackfillService>.Instance);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    private sealed class Fetcher(Func<RemoteRequest, RemoteResponse> fetch) : IRemoteFetcher
    {
        public int Calls { get; private set; }
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken)
        { Calls++; cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(fetch(request)); }
    }
    private static Source Source() => new() { Name = "TEST FIXTURE archive", Kind = "openalex", Query = "consciousness", BackfillLimit = 2 };
    private static RemoteResponse Page(RemoteRequest request) => new(200, request.Url, "application/json", Encoding.UTF8.GetBytes("""
        {"meta":{"next_cursor":"TEST-FIXTURE-next"},"results":[
          {"id":"https://openalex.org/W100","display_name":"TEST FIXTURE old paper 100","publication_date":"2005-10-15","doi":"https://doi.org/10.1234/fixture100"},
          {"id":"https://openalex.org/W101","display_name":"TEST FIXTURE old paper 101","publication_date":"2005-10-14","doi":"https://doi.org/10.1234/fixture101"}
        ]}
        """), null, null);

    [Fact]
    public async Task Daily_batch_is_bounded_and_durable_across_context_recreation()
    {
        await using var store = new Store();
        var source = Source(); source.LastError = "Fresh feed error"; source.FailureCount = 4;
        var nextFresh = source.NextSyncAt;
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        var fetcher = new Fetcher(Page); var before = DateTimeOffset.UtcNow;
        var job = await store.Service(fetcher).RunAsync(source, default);
        Assert.Equal("completed", job.Status); Assert.Equal("backfill", job.Kind);
        Assert.InRange(job.Imported, 1, 2);
        Assert.InRange(await store.Db.Items.CountAsync(), 1, 2);
        Assert.NotNull(source.BackfillStateJson);
        Assert.True(source.BackfillNextAt >= before.AddDays(1));
        Assert.Equal(nextFresh, source.NextSyncAt); Assert.Equal("Fresh feed error", source.LastError); Assert.Equal(4, source.FailureCount);
        var cursor = source.BackfillStateJson; var calls = fetcher.Calls;
        await using var recreated = store.NewContext();
        var resumed = await recreated.Sources.SingleAsync();
        var skipped = await store.Service(fetcher, recreated).RunAsync(resumed, default);
        Assert.Equal("skipped", skipped.Status); Assert.Equal(calls, fetcher.Calls);
        Assert.Equal(cursor, resumed.BackfillStateJson);
        Assert.Equal(job.Imported, resumed.BackfillImportedCount);
        Assert.Single(await recreated.Jobs.ToListAsync());
    }

    [Fact]
    public async Task Failure_preserves_cursor_backs_off_and_does_not_block_healthy_source()
    {
        await using var store = new Store();
        var failed = Source(); var healthy = Source(); healthy.Name += " healthy";
        store.Db.Sources.AddRange(failed, healthy); await store.Db.SaveChangesAsync();
        var seed = await store.Service(new Fetcher(Page)).RunAsync(failed, default);
        Assert.Equal("completed", seed.Status);
        var cursor = failed.BackfillStateJson; var fresh = failed.NextSyncAt;
        var failure = new Fetcher(_ => throw new InvalidOperationException("TEST FIXTURE PRIVATE secret"));
        var before = DateTimeOffset.UtcNow;
        var job = await store.Service(failure).RunAsync(failed, default, force: true);
        Assert.Equal("failed", job.Status); Assert.DoesNotContain("PRIVATE", job.Error);
        Assert.Equal(cursor, failed.BackfillStateJson); Assert.Equal(fresh, failed.NextSyncAt);
        Assert.Equal(1, failed.BackfillFailureCount); Assert.True(failed.BackfillNextAt >= before.AddDays(1));
        var retryAt = failed.BackfillNextAt;
        await store.Service(failure).RunAsync(failed, default, force: true);
        Assert.Equal(2, failed.BackfillFailureCount); Assert.True(failed.BackfillNextAt > retryAt!.Value.AddHours(23));
        Assert.Equal("completed", (await store.Service(new Fetcher(Page)).RunAsync(healthy, default)).Status);
        Assert.Null(healthy.LastError); Assert.Equal(0, healthy.FailureCount);
    }

    [Fact]
    public async Task Partial_database_failure_keeps_cursor_and_retry_deduplicates_committed_imports()
    {
        await using var store = new Store(); var source = Source();
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        await store.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fixture_reject_second BEFORE INSERT ON "Items"
            WHEN NEW."ProviderId" = 'W101' BEGIN SELECT RAISE(ABORT, 'TEST FIXTURE reject second'); END;
            """);
        var first = await store.Service(new Fetcher(Page)).RunAsync(source, default);
        Assert.Equal("failed", first.Status); Assert.Equal(1, first.Imported);
        Assert.Null(source.BackfillStateJson); Assert.Equal(1, await store.Db.Items.CountAsync());
        Assert.Equal(1, source.BackfillImportedCount);
        await store.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fixture_reject_second;");
        var retry = await store.Service(new Fetcher(Page)).RunAsync(source, default, force: true);
        Assert.Equal("completed", retry.Status);
        Assert.Equal(0, source.BackfillFailureCount);
        Assert.NotNull(source.BackfillStateJson);
        Assert.Equal(source.BackfillImportedCount, await store.Db.Items.CountAsync());
        Assert.Equal(1, await store.Db.Items.CountAsync(x => x.ProviderId == "W100"));
    }

    [Fact]
    public async Task Caller_cancellation_records_interruption_without_advancing_archive_progress()
    {
        await using var store = new Store(); var source = Source();
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var fetcher = new Fetcher(_ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.Service(fetcher).RunAsync(source, cancellation.Token));
        Assert.Null(source.BackfillStateJson); Assert.Equal("interrupted", source.BackfillStatus);
        Assert.Equal("interrupted", (await store.Db.Jobs.SingleAsync()).Status);
        Assert.True(source.BackfillNextAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Provider_errors_redact_configured_keys_and_encoded_keys()
    {
        await using var store = new Store(); var source = Source();
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        var fetcher = new Fetcher(_ => throw new RemoteFetchException("HTTP 429 TEST FIXTURE key TEST%20FIXTURE%20key"));
        var job = await store.Service(fetcher).RunAsync(source, default);
        Assert.Equal("failed", job.Status);
        Assert.DoesNotContain("TEST FIXTURE key", job.Error);
        Assert.DoesNotContain("TEST%20FIXTURE%20key", job.Error);
        Assert.Contains("[redacted]", job.Error);
        Assert.Equal(job.Error, source.BackfillMessage);
    }

    [Theory]
    [InlineData("openalex", "needs-key")]
    [InlineData("web-archive", "unsupported")]
    public async Task Unavailable_archive_capability_records_attempt_without_false_success(string kind, string status)
    {
        await using var store = new Store(); var source = Source(); source.Kind = kind;
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        var fetcher = new Fetcher(_ => throw new InvalidOperationException("Must not fetch"));
        var job = await store.Service(fetcher, key: null).RunAsync(source, default);
        Assert.Equal("completed", job.Status); Assert.Equal(status, source.BackfillStatus);
        Assert.NotNull(source.BackfillLastAttemptAt); Assert.Null(source.BackfillLastSuccessAt);
        Assert.Equal(0, job.Imported); Assert.Equal(0, fetcher.Calls);
        Assert.True(source.BackfillNextAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Unavailable_feed_does_not_claim_a_successful_archive_retrieval()
    {
        await using var store = new Store();
        var source = new Source { Name = "TEST FIXTURE unavailable", Kind = "rss", Url = "https://example.org/feed" };
        store.Db.Add(source); await store.Db.SaveChangesAsync();
        var fetcher = new Fetcher(request => request.Url.AbsolutePath == "/robots.txt"
            ? new RemoteResponse(200, request.Url, "text/plain", Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /"), null, null)
            : throw new InvalidOperationException("A disallowed feed must not be fetched"));
        var job = await store.Service(fetcher).RunAsync(source, default);
        Assert.Equal("unavailable", source.BackfillStatus);
        Assert.NotNull(source.BackfillLastAttemptAt); Assert.Null(source.BackfillLastSuccessAt);
        Assert.Null(source.LastAttemptAt); Assert.Equal(0, job.Imported); Assert.Equal(1, fetcher.Calls);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task Disabled_or_paused_backfill_never_fetches_even_when_forced(bool enabled, bool archiveEnabled, bool paused)
    {
        await using var store = new Store(); var source = Source();
        source.Enabled = enabled; source.BackfillEnabled = archiveEnabled;
        store.Db.Sources.Add(source); await store.Db.SaveChangesAsync();
        var fetcher = new Fetcher(_ => throw new InvalidOperationException("Must not fetch"));
        var job = await store.Service(fetcher, paused: paused).RunAsync(source, default, force: true);
        Assert.Equal("skipped", job.Status); Assert.Equal(0, fetcher.Calls);
        Assert.Null(source.BackfillLastAttemptAt); Assert.Empty(await store.Db.Jobs.ToListAsync());
    }
}
