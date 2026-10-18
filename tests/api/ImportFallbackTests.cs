using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitHole.Data;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class ImportFallbackTests
{
    private sealed class Fetcher(Func<RemoteRequest, RemoteResponse> fetch) : IRemoteFetcher
    {
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken) => Task.FromResult(fetch(request));
    }

    [Theory]
    [InlineData("https://example.org/slow", "manual")]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk", "youtube")]
    [InlineData("https://soundcloud.com/test/track", "manual")]
    public async Task MetadataDeadlineStillPersistsLinkOnlyRecord(string url, string provider)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var fetcher = new Fetcher(request => request.Url.AbsolutePath == "/robots.txt"
            ? new RemoteResponse(200, request.Url, "text/plain", Encoding.UTF8.GetBytes("User-agent: *\nAllow: /"), null, null)
            : throw new OperationCanceledException("Internal HTTP deadline expired"));
        var service = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var imported = await service.ImportUrlAsync(url, "TEST FIXTURE slow metadata", default);
        Assert.Equal(provider, imported.Provider);
        Assert.Equal("TEST FIXTURE slow metadata", imported.Title);
        Assert.Equal("Link only: The remote request timed out.", imported.ImportWarning);
        Assert.Null(imported.Body);
        Assert.Equal("metadata", imported.TextProvenance);
        Assert.Equal(imported.Id, (await db.Items.SingleAsync()).Id);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutPersistingAnImport()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        using var cancellation = new CancellationTokenSource();
        var fetcher = new Fetcher(_ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); });
        var service = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportUrlAsync("https://example.org/cancelled", null, cancellation.Token));
        Assert.Empty(await db.Items.ToListAsync());
    }

    [Fact]
    public async Task Selected_feed_entry_preserves_real_metadata_without_subscribing_or_importing_other_entries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var fetcher = new Fetcher(request => new RemoteResponse(200, request.Url, "application/rss+xml", Encoding.UTF8.GetBytes("""
            <rss><channel><item><title>TEST real title</title><link>https://example.org/selected</link><author>Ada</author><pubDate>Fri, 16 Oct 2026 12:00:00 GMT</pubDate><description>Supplied excerpt</description></item>
            <item><title>TEST unselected</title><link>https://example.org/unselected</link></item></channel></rss>
            """), null, null));
        var service = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var item = await service.ImportUrlAsync("https://example.org/selected", "Caller cannot replace feed text", default, "https://example.org/feed");
        Assert.Equal("TEST real title", item.Title);
        Assert.Equal("Ada", item.Author);
        Assert.Equal("Supplied excerpt", item.Excerpt);
        Assert.Equal(2026, item.PublishedAt!.Value.Year);
        Assert.Contains("importedFromFeed", item.MetadataJson!);
        Assert.Empty(await db.Sources.ToListAsync());
        Assert.Equal(item.Id, (await service.ImportUrlAsync("https://example.org/selected", null, default, "https://example.org/feed")).Id);
        Assert.Single(await db.Items.ToListAsync());
        await Assert.ThrowsAsync<RemoteFetchException>(() => service.ImportUrlAsync("https://example.org/not-in-feed", null, default, "https://example.org/feed"));
        Assert.Single(await db.Items.ToListAsync());
    }
}
