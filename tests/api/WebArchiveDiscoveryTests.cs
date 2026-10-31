using System.Net;
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

public class WebArchiveDiscoveryTests
{
    private sealed class Remote : IRemoteFetcher
    {
        public List<Uri> Requests { get; } = [];
        public bool FailIndex;
        public bool Empty;
        public bool MissingPage;
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Requests.Add(request.Url);
            var path = request.Url.AbsolutePath;
            var type = "text/html";
            string body;
            if (request.Url.Host == "hn.algolia.com")
            {
                if (FailIndex) throw new RemoteFetchException("Remote provider returned HTTP 429.");
                type = "application/json";
                body = Empty ? "{\"nbPages\":0,\"hits\":[]}" : JsonSerializer.Serialize(new
                {
                    nbPages = 1,
                    hits = new[] {
                        new { objectID = "1", title = "How digital filters work (1999)", url = "https://example.org/known-date", author = "NOT-THE-AUTHOR", created_at_i = 1262304000 },
                        new { objectID = "2", title = "How waveforms work (1988)", url = "https://example.org/unknown-date", author = "NOT-THE-AUTHOR", created_at_i = 1262304000 },
                        new { objectID = "3", title = "How an old link changed", url = "https://example.org/replaced", author = "NOT-THE-AUTHOR", created_at_i = 1262304000 },
                        new { objectID = "4", title = "Introducing a new AI model", url = "https://example.org/announcement", author = "NOT-THE-AUTHOR", created_at_i = 1262304000 },
                        new { objectID = "5", title = "How to read a private file", url = "http://127.0.0.1/private", author = "NOT-THE-AUTHOR", created_at_i = 1262304000 },
                        new { objectID = "6", title = "How digital filters work", url = "https://example.org/known-date", author = "NOT-THE-AUTHOR", created_at_i = 1262304000 }
                    }
                });
            }
            else if (path == "/robots.txt") { type = "text/plain"; body = "User-agent: *\nAllow: /"; }
            else if (path == "/known-date") body = "<title>How digital filters work</title><meta name='author' content='Original author'/><meta property='article:published_time' content='2001-02-03T00:00:00Z'/><meta name='description' content='An original explanation of waveforms and digital filters.'/ >";
            else if (path == "/unknown-date") throw new RemoteFetchException("Remote provider returned HTTP " + (MissingPage ? "404." : "403."));
            else if (path == "/replaced") body = "<title>How this site works now</title><meta property='article:published_time' content='" + DateTimeOffset.UtcNow.ToString("O") + "'/>";
            else throw new InvalidOperationException("Unexpected request: " + request.Url);
            return Task.FromResult(new RemoteResponse(200, request.Url, type, Encoding.UTF8.GetBytes(body), null, null));
        }
    }

    [Fact]
    public void Search_is_topic_scoped_bounded_and_uses_submission_dates_only_for_discovery()
    {
        var end = DateTimeOffset.Parse("2015-01-01T00:00:00Z");
        var url = WebArchiveDiscovery.SearchUrl("consciousness & query=private", end, end.AddYears(-1), 99999);
        Assert.Equal("hn.algolia.com", url.Host);
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal("consciousness & query=private", query["query"]);
        Assert.Equal("49", query["page"]); Assert.Equal("20", query["hitsPerPage"]);
        Assert.Equal("title", query["restrictSearchableAttributes"]);
        Assert.Contains("created_at_i<", query["numericFilters"].ToString());
    }

    [Fact]
    public async Task Online_discovery_fetches_unseen_originals_preserves_real_dates_and_is_idempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var topic = new Topic { Name = "TEST FIXTURE Sound" };
        var source = new Source { Name = "TEST FIXTURE older web", Kind = "web-archive", Query = "digital filters", ResultLimit = 3, IntervalMinutes = 1440, TopicIds = [topic.Id], Enabled = false };
        db.AddRange(topic, source); await db.SaveChangesAsync();
        var remote = new Remote();
        var service = new IngestionService(db, remote, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var preview = await service.FetchSourceAsync(source, default);
        Assert.Equal(2, preview.Items.Count); Assert.Empty(await db.Items.ToListAsync()); Assert.Empty(await db.Jobs.ToListAsync());
        var job = await service.RefreshAsync(source, default);
        Assert.Equal("completed", job.Status); Assert.Equal(2, job.Imported);
        var known = await db.Items.SingleAsync(i => i.CanonicalUrl.EndsWith("/known-date"));
        var unknown = await db.Items.SingleAsync(i => i.CanonicalUrl.EndsWith("/unknown-date"));
        Assert.Equal("Original author", known.Author); Assert.Equal(2001, known.PublishedAt!.Value.Year);
        Assert.Equal("How digital filters work", known.Title); Assert.Null(known.Body);
        Assert.Null(unknown.PublishedAt); Assert.Null(unknown.Author);
        Assert.Equal("How waveforms work", unknown.Title); Assert.Contains("Link only", unknown.ImportWarning);
        Assert.Contains("https://news.ycombinator.com/item?id=", known.MetadataJson);
        Assert.All(new[] { known, unknown }, i => { Assert.True(i.IsPublic); Assert.Contains(topic.Id, i.TopicIds); });
        db.States.Add(new ItemState { ItemId = known.Id, Saved = true, Read = true });
        known.IsPublic = false; await db.SaveChangesAsync();
        remote.Requests.Clear();
        var repeated = await service.RefreshAsync(source, default);
        Assert.Equal("completed", repeated.Status); Assert.Equal(0, repeated.Imported);
        Assert.DoesNotContain(remote.Requests, u => u.AbsolutePath is "/known-date" or "/unknown-date" or "/announcement" or "/private");
        Assert.Equal(2, await db.Items.CountAsync()); Assert.False(known.IsPublic); Assert.True((await db.States.SingleAsync()).Read);
        remote.FailIndex = true;
        var failed = await service.RefreshAsync(source, default);
        Assert.Equal("failed", failed.Status); Assert.NotNull(source.LastError); Assert.True(source.NextSyncAt > DateTimeOffset.UtcNow);
        Assert.Equal(2, await db.Items.CountAsync());
    }

    [Fact]
    public async Task Confirmed_dead_pages_are_not_imported_as_archive_links()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new IngestionService(db, new Remote { MissingPage = true }, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var batch = await service.FetchSourceAsync(new Source { Kind = "web-archive", Query = "digital filters", ResultLimit = 3 }, default);
        Assert.Single(batch.Items); Assert.EndsWith("/known-date", batch.Items[0].CanonicalUrl);
    }

    [Fact]
    public async Task Empty_historical_window_has_one_bounded_fallback_and_cancellation_stops_requests()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var remote = new Remote { Empty = true };
        var service = new IngestionService(db, remote, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var source = new Source { Kind = "web-archive", Query = "remote viewing", ResultLimit = 3 };
        Assert.Empty((await service.FetchSourceAsync(source, default)).Items); Assert.Equal(2, remote.Requests.Count);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FetchSourceAsync(source, cancellation.Token));
        Assert.Equal(2, remote.Requests.Count);
    }
}
