using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Ingestion;
using System.Net;
using System.Text.Json;
using Xunit;

namespace RabbitHole.Tests;

public class IngestionTests
{
    private const string Feed = """
        <rss version="2.0"><channel><title>Fixture</title>
          <item><guid>first</guid><title>A real fixture article</title>
          <link>https://example.org/article</link><description>Known excerpt</description>
          <pubDate>Tue, 14 Oct 2025 12:00:00 GMT</pubDate></item>
        </channel></rss>
        """;

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://localhost/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://0177.0.0.1/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://0x7f000001/")]
    [InlineData("http://example.com:8080/")]
    [InlineData("https://name:secret@public.example/")]
    [InlineData("file:///etc/passwd")]
    public void PrivateOrNonHttpUrlsAreBlocked(string url) => Assert.Throws<RemoteFetchException>(() => SafeRemoteFetcher.ValidateUrl(url));

    [Fact]
    public void DnsPrivateAndReservedAddressesAreBlocked()
    {
        Assert.False(SafeRemoteFetcher.IsPublic(IPAddress.Parse("10.2.3.4")));
        Assert.False(SafeRemoteFetcher.IsPublic(IPAddress.Parse("::1")));
        Assert.False(SafeRemoteFetcher.IsPublic(IPAddress.Parse("fc00::1")));
        Assert.False(SafeRemoteFetcher.IsPublic(IPAddress.Parse("2002:7f00:1::")));
        Assert.False(SafeRemoteFetcher.IsPublic(IPAddress.Parse("2001::1")));
        Assert.True(SafeRemoteFetcher.IsPublic(IPAddress.Parse("8.8.8.8")));
    }

    [Fact]
    public void RedirectsRevalidateDestinationAndEmbedsUseApprovedHosts()
    {
        var origin = new Uri("https://example.org/feed");
        Assert.Throws<RemoteFetchException>(() => SafeRemoteFetcher.ValidateRedirect(origin, new Uri("http://127.0.0.1/private")));
        Assert.Throws<RemoteFetchException>(() => SafeRemoteFetcher.ValidateRedirect(origin, new Uri("https://example.net:8443/feed")));
        Assert.Throws<RemoteFetchException>(() => SafeRemoteFetcher.ValidateRedirect(origin, new Uri("https://user:pass@example.net/feed")));
        Assert.False(ProviderAdapters.IsSoundCloudHost("evil-soundcloud.com"));
        Assert.False(ProviderAdapters.IsApprovedEmbed("https://evil.youtube-nocookie.com/embed/abcdefghijk"));
        Assert.False(ProviderAdapters.IsApprovedEmbed("https://w.soundcloud.com.evil.com/player/"));
        Assert.True(ProviderAdapters.IsApprovedEmbed("https://www.youtube-nocookie.com/embed/abcdefghijk"));
    }

    [Fact]
    public async Task ResolvedPrivateAddressIsRejectedBeforeConnection()
    {
        var fetcher = new SafeRemoteFetcher(new FixedDns(IPAddress.Parse("169.254.169.254")));
        await Assert.ThrowsAsync<RemoteFetchException>(() => fetcher.FetchAsync(
            new RemoteRequest(new Uri("https://rebinding.example/metadata"), ["text/html"]), CancellationToken.None));
    }

    [Fact]
    public void FeedParserRejectsEntityExpansionAndPreservesUnknownDate()
    {
        Assert.ThrowsAny<Exception>(() => ProviderAdapters.ParseFeed("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><rss>&x;</rss>"));
        var items = ProviderAdapters.ParseFeed(Feed.Replace("<pubDate>Tue, 14 Oct 2025 12:00:00 GMT</pubDate>", ""));
        Assert.Single(items);
        Assert.Null(items[0].PublishedAt);
        Assert.Equal("https://example.org/article", items[0].CanonicalUrl);
    }

    [Fact]
    public void FeedSuppliedFullTextStaysSeparateFromExcerpt()
    {
        var xml = """
            <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/"><channel>
              <item><title>Test</title><link>https://example.org/full</link>
                <description>Teaser</description><content:encoded><![CDATA[<p>Full source text</p>]]></content:encoded>
              </item>
            </channel></rss>
            """;
        var item = Assert.Single(ProviderAdapters.ParseFeed(xml));
        Assert.Equal("Teaser", item.Excerpt);
        Assert.Equal("Full source text", item.Body);
        Assert.Equal("feed-content", item.TextProvenance);
    }

    [Fact]
    public void EmptyFeedContentDoesNotClaimFullText()
    {
        var xml = """
            <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/"><channel>
              <item><title>Test</title><link>https://example.org/metadata-only</link>
                <description>Teaser</description><content:encoded><![CDATA[ ]]></content:encoded>
              </item>
            </channel></rss>
            """;
        var item = Assert.Single(ProviderAdapters.ParseFeed(xml));
        Assert.Null(item.Body);
        Assert.Equal("metadata", item.TextProvenance);
    }

    [Fact]
    public void AcademicDoiIdentifiersNormalizeAcrossProviders()
    {
        Assert.Equal("10.1234/example", ProviderAdapters.NormalizeDoi("https://doi.org/10.1234/EXAMPLE"));
        Assert.Equal("10.1234/example", ProviderAdapters.NormalizeDoi("10.1234/example"));
        var arxiv = """
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:arxiv="http://arxiv.org/schemas/atom">
              <entry><id>https://arxiv.org/abs/2510.12345v2</id><title>Paper</title>
                <link href="https://arxiv.org/abs/2510.12345v2" rel="alternate" />
                <arxiv:doi>10.1234/EXAMPLE</arxiv:doi></entry>
            </feed>
            """;
        var item = Assert.Single(ProviderAdapters.ParseArxiv(arxiv));
        Assert.Equal("10.1234/example", item.Doi);
        Assert.Equal("2510.12345", item.ArxivId);
    }

    [Fact]
    public void OpenAlexKeepsExactSourceReportedDate()
    {
        var item = Assert.Single(ProviderAdapters.ParseOpenAlex("""
            {"id":"https://openalex.org/W123","title":"Paper","publication_date":"2007-06-01"}
            """));
        Assert.Contains("\"sourcePublishedDate\":\"2007-06-01\"", item.MetadataJson);
        Assert.Equal(2007, item.PublishedAt?.Year);
    }

    [Fact]
    public void OpenAlexReconstructsSourceAbstractAndRetainsValidatedResearchLinks()
    {
        var item = Assert.Single(ProviderAdapters.ParseOpenAlex("""
            {
              "id":"https://openalex.org/W123", "title":"Paper",
              "abstract_inverted_index":{"An":[0],"actual":[1],"abstract.":[2]},
              "best_oa_location":{"landing_page_url":"https://repository.example.org/paper"},
              "open_access":{"is_oa":true,"oa_status":"green","oa_url":"https://backup.example.org/paper"},
              "cited_by_count":12,
              "referenced_works":["https://openalex.org/W5","https://openalex.org/W6","https://openalex.org/W7?fake=1","http://127.0.0.1/W8"]
            }
            """));
        Assert.Equal("An actual abstract.", item.Excerpt);
        Assert.Equal("An actual abstract.", item.Body);
        Assert.Equal("provider-abstract", item.TextProvenance);
        using var metadata = JsonDocument.Parse(item.MetadataJson!);
        var root = metadata.RootElement;
        Assert.Equal("https://repository.example.org/paper", root.GetProperty("openAccessUrl").GetString());
        Assert.Equal("green", root.GetProperty("openAccessStatus").GetString());
        Assert.Equal(12, root.GetProperty("citedByCount").GetInt32());
        Assert.Equal(2, root.GetProperty("referencedWorksCount").GetInt32());
        Assert.Equal(new[] { "https://openalex.org/W5", "https://openalex.org/W6" },
            root.GetProperty("referencedWorks").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"word\":[1]}")]
    [InlineData("{\"word\":[0,0]}")]
    [InlineData("{\"word\":[5000]}")]
    [InlineData("{\"word\":[\"0\"]}")]
    public void OpenAlexRejectsMissingMalformedOrUnboundedAbstractIndexes(string index)
    {
        var item = Assert.Single(ProviderAdapters.ParseOpenAlex(
            "{\"id\":\"https://openalex.org/W123\",\"abstract_inverted_index\":" + index + "}"));
        Assert.Null(item.Excerpt);
        Assert.Null(item.Body);
        Assert.Equal("metadata", item.TextProvenance);
    }

    [Fact]
    public void OpenAlexRejectsPrivateOpenAccessUrlAndFallsBackToPublicOfficialUrl()
    {
        var item = Assert.Single(ProviderAdapters.ParseOpenAlex("""
            {"id":"https://openalex.org/W123",
             "best_oa_location":{"landing_page_url":"http://127.0.0.1/private"},
             "open_access":{"oa_url":"https://public.example.org/oa"}}
            """));
        using var metadata = JsonDocument.Parse(item.MetadataJson!);
        Assert.Equal("https://public.example.org/oa", metadata.RootElement.GetProperty("openAccessUrl").GetString());
    }

    [Fact]
    public void OpenAlexCapsReferenceMetadataAndReportsTruncation()
    {
        var references = string.Join(',', Enumerable.Range(1, 101).Select(id => $"\"https://openalex.org/W{id}\""));
        var item = Assert.Single(ProviderAdapters.ParseOpenAlex(
            "{\"id\":\"https://openalex.org/W123\",\"referenced_works\":[" + references + "]}"));
        using var metadata = JsonDocument.Parse(item.MetadataJson!);
        Assert.Equal(100, metadata.RootElement.GetProperty("referencedWorks").GetArrayLength());
        Assert.Equal(101, metadata.RootElement.GetProperty("referencedWorksCount").GetInt32());
        Assert.True(metadata.RootElement.GetProperty("referencedWorksTruncated").GetBoolean());
    }

    [Fact]
    public void XmlBytesRespectDeclaredFeedEncoding()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"iso-8859-1\"?><rss><channel><item><title>Café</title><link>https://example.org/cafe</link></item></channel></rss>";
        var item = Assert.Single(ProviderAdapters.ParseFeed(System.Text.Encoding.Latin1.GetBytes(xml)));
        Assert.Equal("Café", item.Title);
    }

    [Fact]
    public void AutodiscoveryRejectsPrivateTarget()
    {
        var links = ProviderAdapters.DiscoverFeeds("""
            <link rel="alternate" type="application/rss+xml" href="http://127.0.0.1/feed" />
            <link rel="alternate" type="application/atom+xml" href="/atom.xml" />
            """, new Uri("https://example.org/blog"));
        Assert.Single(links);
        Assert.Equal("https://example.org/atom.xml", links[0].ToString());
    }

    [Fact]
    public async Task RefreshIsIdempotentAndPreservesReadState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var source = new Source { Name = "Fixture", Kind = "rss", Url = "https://example.org/feed" };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        var fetcher = new FixtureFetcher(new RemoteResponse(200, new Uri(source.Url), "application/rss+xml",
            System.Text.Encoding.UTF8.GetBytes(Feed), "etag-1", null));
        var service = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var first = await service.RefreshAsync(source, CancellationToken.None);
        Assert.Equal("completed", first.Status);
        Assert.Equal(1, first.Imported);
        var item = await db.Items.SingleAsync();
        db.States.Add(new ItemState { ItemId = item.Id, Read = true });
        await db.SaveChangesAsync();
        var second = await service.RefreshAsync(source, CancellationToken.None);
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, await db.Items.CountAsync());
        Assert.True((await db.States.SingleAsync()).Read);
        Assert.Equal("etag-1", source.ETag);
    }

    [Fact]
    public async Task ProviderFailureSetsRetryableHealthWithoutDeletingItems()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var source = new Source { Name = "Fixture", Kind = "rss", Url = "https://example.org/feed" };
        db.Sources.Add(source);
        db.Items.Add(new ContentItem { CanonicalUrl = "https://example.org/item", Title = "Existing", SourceId = source.Id });
        await db.SaveChangesAsync();
        var service = new IngestionService(db, new FixtureFetcher(new RemoteFetchException("HTTP 429")),
            new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var job = await service.RefreshAsync(source, CancellationToken.None);
        Assert.Equal("failed", job.Status);
        Assert.Equal(1, source.FailureCount);
        Assert.True(source.NextSyncAt > DateTimeOffset.UtcNow);
        Assert.Equal(1, await db.Items.CountAsync());
        var firstRetry = source.NextSyncAt;
        await service.RefreshAsync(source, CancellationToken.None);
        Assert.Equal(2, source.FailureCount);
        Assert.True(source.NextSyncAt > firstRetry.AddMinutes(300));
    }

    [Fact]
    public async Task RemoteTimeoutIsRecordedAsRetryableFailure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var source = new Source { Name = "Timeout", Url = "https://example.org/feed" };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        var service = new IngestionService(db, new FixtureFetcher(new OperationCanceledException()),
            new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var job = await service.RefreshAsync(source, CancellationToken.None);
        Assert.Equal("failed", job.Status);
        Assert.Contains("timed out", job.Error);
        Assert.True(source.NextSyncAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task FailedSourceDoesNotBlockHealthySource()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var bad = new Source { Name = "Bad", Url = "https://bad.example/feed" };
        var good = new Source { Name = "Good", Url = "https://good.example/feed" };
        db.Sources.AddRange(bad, good);
        await db.SaveChangesAsync();
        var fetcher = new RoutedFetcher(request => request.Url.Host == "bad.example"
            ? throw new RemoteFetchException("Remote source returned HTTP 429.")
            : new RemoteResponse(200, request.Url, "application/rss+xml", System.Text.Encoding.UTF8.GetBytes(Feed), null, null));
        var service = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        Assert.Equal("failed", (await service.RefreshAsync(bad, CancellationToken.None)).Status);
        Assert.Equal("completed", (await service.RefreshAsync(good, CancellationToken.None)).Status);
        Assert.Equal(1, await db.Items.CountAsync());
    }

    [Fact]
    public async Task NotModifiedRefreshIsSuccessfulAndKeepsItems()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var source = new Source { Name = "Cached", Url = "https://example.org/feed", ETag = "etag-1" };
        db.Sources.Add(source);
        db.Items.Add(new ContentItem { CanonicalUrl = "https://example.org/article", Title = "Existing", SourceId = source.Id });
        await db.SaveChangesAsync();
        var response = new RemoteResponse(304, new Uri(source.Url), "", [], "etag-1", null);
        var service = new IngestionService(db, new FixtureFetcher(response), new ConfigurationBuilder().Build(),
            NullLogger<IngestionService>.Instance);
        var job = await service.RefreshAsync(source, CancellationToken.None);
        Assert.Equal("completed", job.Status);
        Assert.Equal(0, job.Imported);
        Assert.Equal(1, await db.Items.CountAsync());
        Assert.Null(source.LastError);
    }

    [Fact]
    public async Task ConcurrentRefreshesDeduplicateWithoutErasingPersonalState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var source = new Source { Name = "Fixture", Url = "https://example.org/feed" };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        var response = new RemoteResponse(200, new Uri(source.Url), "application/rss+xml",
            System.Text.Encoding.UTF8.GetBytes(Feed), null, null);
        var service = new IngestionService(db, new FixtureFetcher(response), new ConfigurationBuilder().Build(),
            NullLogger<IngestionService>.Instance);
        await Task.WhenAll(service.RefreshAsync(source, CancellationToken.None), service.RefreshAsync(source, CancellationToken.None));
        Assert.Equal(1, await db.Items.CountAsync());
        Assert.Equal(2, await db.Jobs.CountAsync());
    }

    [Fact]
    public async Task DoiMergeKeepsBothProviderReferencesAndNotes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var service = new IngestionService(db, new FixtureFetcher(new RemoteFetchException("unused")),
            new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var first = (await service.UpsertAsync(new NormalizedItem
        {
            Provider = "openalex",
            ProviderId = "W123",
            CanonicalUrl = "https://openalex.org/W123",
            Doi = "10.1234/example",
            Title = "A paper"
        }, null, [], CancellationToken.None)).Item;
        db.States.Add(new ItemState { ItemId = first.Id, Saved = true, Read = true });
        db.Notes.Add(new Note { ItemId = first.Id, Body = "My note" });
        await db.SaveChangesAsync();
        await service.UpsertAsync(new NormalizedItem
        {
            Provider = "arxiv",
            ProviderId = "2501.12345",
            ArxivId = "2501.12345",
            Doi = "10.1234/example",
            CanonicalUrl = "https://arxiv.org/abs/2501.12345",
            Title = "A paper"
        }, null, [], CancellationToken.None);
        var merged = await db.Items.SingleAsync();
        Assert.Equal(first.Id, merged.Id);
        Assert.Equal("2501.12345", merged.ArxivId);
        Assert.Contains("openalex", merged.MetadataJson);
        Assert.Contains("arxiv", merged.MetadataJson);
        Assert.True((await db.States.SingleAsync()).Saved);
        Assert.Equal("My note", (await db.Notes.SingleAsync()).Body);
    }

    [Fact]
    public async Task ResearchImportRejectsMismatchedProviderRecord()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var response = new RemoteResponse(200, new Uri("https://api.openalex.org/works/W456"), "application/json",
            System.Text.Encoding.UTF8.GetBytes("{\"id\":\"https://openalex.org/W456\",\"title\":\"Other\"}"), null, null);
        var service = new IngestionService(db, new FixtureFetcher(response), new ConfigurationBuilder().Build(),
            NullLogger<IngestionService>.Instance);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ImportResearchAsync("openalex", "W123", CancellationToken.None));
        Assert.Equal(0, await db.Items.CountAsync());
    }

    [Fact]
    public async Task RobotsDisallowProducesHonestLinkOnlyItem()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var response = new RemoteResponse(200, new Uri("https://example.org/robots.txt"), "text/plain",
            System.Text.Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /article"), null, null);
        var service = new IngestionService(db, new FixtureFetcher(response), new ConfigurationBuilder().Build(),
            NullLogger<IngestionService>.Instance);
        var item = await service.ImportUrlAsync("https://example.org/article", null, CancellationToken.None);
        Assert.Null(item.Body);
        Assert.Contains("Link only", item.ImportWarning);
        Assert.Null(item.PublishedAt);
    }

    [Fact]
    public async Task YoutubeUrlUsesFirstPartyOEmbedAndKeepsLinkOnlyFallback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var requested = new List<Uri>();
        var fetcher = new RoutedFetcher(request =>
        {
            requested.Add(request.Url);
            if (request.Url.Query.Contains("ZYXWVUTSRQP")) throw new RemoteFetchException("Remote source returned HTTP 404.");
            return new RemoteResponse(200, request.Url, "application/json",
                System.Text.Encoding.UTF8.GetBytes("{\"title\":\"Real video title\",\"author_name\":\"Real channel\",\"html\":\"<script>ignore</script>\"}"), null, null);
        });
        var service = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var item = await service.ImportUrlAsync("https://youtu.be/abcdefghijk", null, CancellationToken.None);
        Assert.Single(requested);
        Assert.Equal("/oembed", requested[0].AbsolutePath);
        Assert.Equal("Real video title", item.Title);
        Assert.Equal("Real channel", item.Author);
        Assert.Equal("https://www.youtube-nocookie.com/embed/abcdefghijk", item.EmbedUrl);
        Assert.Null(item.ImportWarning);
        Assert.Null(item.Body);
        var fallback = await service.ImportUrlAsync("https://youtu.be/ZYXWVUTSRQP", null, CancellationToken.None);
        Assert.Equal("video", fallback.Kind);
        Assert.Equal("ZYXWVUTSRQP", fallback.ProviderId);
        Assert.Contains("Link only", fallback.ImportWarning);
        Assert.Equal("https://www.youtube-nocookie.com/embed/ZYXWVUTSRQP", fallback.EmbedUrl);
    }

    private sealed class FixtureFetcher : IRemoteFetcher
    {
        private readonly RemoteResponse? _response;
        private readonly Exception? _error;
        public FixtureFetcher(RemoteResponse response) => _response = response;
        public FixtureFetcher(Exception error) => _error = error;
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken) =>
            _error is null ? Task.FromResult(_response!) : Task.FromException<RemoteResponse>(_error);
    }
    private sealed class FixedDns(IPAddress address) : IDnsLookup
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) => Task.FromResult(new[] { address });
    }
    private sealed class RoutedFetcher(Func<RemoteRequest, RemoteResponse> route) : IRemoteFetcher
    {
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(route(request));
    }
}
