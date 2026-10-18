using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitHole.Data;
using RabbitHole.Domain;
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Selected_feed_entry_beyond_refresh_limit_is_found_in_direct_and_discovered_feeds(bool atom, bool discover)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var entries = string.Concat(Enumerable.Range(1, 105).Select(i => atom
            ? $"<entry><title>TEST FIXTURE entry {i}</title><link href='https://example.org/item/{i}'/><author><name>Ada</name></author><summary>Supplied excerpt {i}</summary></entry>"
            : $"<item><title>TEST FIXTURE entry {i}</title><link>https://example.org/item/{i}</link><author>Ada</author><description>Supplied excerpt {i}</description></item>"));
        var xml = atom ? "<feed xmlns='http://www.w3.org/2005/Atom'>" + entries + "</feed>"
            : "<rss><channel>" + entries + "</channel></rss>";
        var fetcher = new Fetcher(request =>
        {
            Assert.Equal(1_500_000, request.MaxBytes);
            return request.Url.AbsolutePath == "/home"
                ? new RemoteResponse(200, request.Url, "text/html", Encoding.UTF8.GetBytes("<link rel='alternate' type='application/rss+xml' href='/feed'>"), null, null)
                : new RemoteResponse(200, request.Url, atom ? "application/atom+xml" : "application/rss+xml", Encoding.UTF8.GetBytes(xml), null, null);
        });
        var service = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var feedUrl = discover ? "https://example.org/home" : "https://example.org/feed";
        var refresh = await service.FetchSourceAsync(new Source { Url = feedUrl }, default);
        Assert.Equal(100, refresh.Items.Count);
        Assert.DoesNotContain(refresh.Items, item => item.CanonicalUrl.EndsWith("/101"));

        var selected = await service.ImportUrlAsync("https://example.org/item/101/", null, default, feedUrl);
        Assert.Equal("TEST FIXTURE entry 101", selected.Title);
        Assert.Equal("Ada", selected.Author);
        Assert.Equal("Supplied excerpt 101", selected.Excerpt);
        Assert.Equal("https://example.org/item/101", selected.CanonicalUrl);
        Assert.Empty(await db.Sources.ToListAsync());
        Assert.Equal(selected.Id, (await db.Items.SingleAsync()).Id);
        await Assert.ThrowsAsync<RemoteFetchException>(() => service.ImportUrlAsync("https://example.org/absent", null, default, feedUrl));
        Assert.Single(await db.Items.ToListAsync());
    }
}
