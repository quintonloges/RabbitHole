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

public sealed class RssArchiveBackfillTests
{
    private const string Site = "https://example.org";
    private static string Feed(int count = 1, string path = "") => "<rss version='2.0' xmlns:content='http://purl.org/rss/1.0/modules/content/'><channel><link>" + Site + path + "/</link>" +
        string.Concat(Enumerable.Range(0, count).Select(i => $"<item><guid>entry-{i}</guid><title>Fixture {i}</title><link>{Site}{path}/post-{i}</link><content:encoded>Supplied body {i}</content:encoded></item>")) + "</channel></rss>";
    private static string Html(params int[] numbers) => string.Concat(numbers.Select(i => $"<h2><a href='/old-{i}'>Original title {i}</a></h2>"));
    private static string Sitemap(IEnumerable<string> urls) => "<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>" +
        string.Concat(urls.Select(u => $"<url><loc>{u}</loc><lastmod>2026-11-11</lastmod></url>")) + "</urlset>";

    [Fact]
    public async Task FullCurrentFeedDrainsBeyondRecentCapWithoutLosingSourceBodyOrRepeating()
    {
        await using var env = await Fixture.Create(Feed(106));
        env.Source.BackfillLimit = 2;
        var seen = new HashSet<string>();
        for (var day = 0; day < 53; day++)
        {
            var batch = await env.Engine.FetchAsync(env.Source, default);
            Assert.InRange(batch.Items.Count, 1, 2);
            foreach (var item in batch.Items)
            {
                Assert.True(seen.Add(item.CanonicalUrl));
                Assert.StartsWith("Supplied body", item.Body);
                Assert.Equal("feed-content", item.TextProvenance);
                Assert.Null(item.PublishedAt);
            }
            await env.Commit(batch);
        }
        Assert.Equal(105, seen.Count);
        Assert.Contains(Site + "/post-105", seen);
        Assert.DoesNotContain(env.Fetcher.Requests, r => r.Url.AbsolutePath.StartsWith("/post-"));
    }

    [Fact]
    public async Task HtmlOlderPostsProgressesAcrossDailyBatchesAndPreservesPageRemainder()
    {
        await using var env = await Fixture.Create();
        env.Source.BackfillLimit = 3;
        env.Fetcher.Put("/", Html(1, 2, 3, 4, 5) + "<a href='/page/2'>Older Posts →</a>", "text/html");
        env.Fetcher.Put("/page/2", Html(6, 7, 8, 9) + "<a rel='next' href='/page/3'>Continue</a>", "text/html");
        env.Fetcher.Put("/page/3", Html(10), "text/html");
        env.Fetcher.ArticleFallback = true;
        var urls = new HashSet<string>();
        for (var day = 0; day < 4; day++)
        {
            var batch = await env.Engine.FetchAsync(env.Source, default);
            Assert.InRange(batch.Items.Count, 1, 3);
            foreach (var item in batch.Items) Assert.True(urls.Add(item.CanonicalUrl));
            await env.Commit(batch);
        }
        Assert.Equal(10, urls.Count);
        Assert.Contains(Site + "/old-10", urls);
        Assert.Equal(1, env.Fetcher.Requests.Count(r => r.Url.AbsolutePath == "/sitemap.xml"));
        Assert.True(env.Source.BackfillStateJson!.Length < 10_000);
    }

    [Theory]
    [InlineData("/feed/")]
    [InlineData("/atom/everything/")]
    [InlineData("/feeds/posts/default")]
    [InlineData("/feeds/all.atom.xml")]
    [InlineData("/category/specific/feed/")]
    public async Task ActualFeedMembershipDoesNotInheritFeedEndpointDirectory(string path)
    {
        await using var env = await Fixture.Create();
        env.Source.Url = Site + path;
        env.Fetcher.Put(path, Feed(3));
        var batch = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal(2, batch.Items.Count);
        Assert.All(batch.Items, i => Assert.StartsWith(Site + "/post-", i.CanonicalUrl));
    }

    [Fact]
    public async Task SeparatelyHostedFeedBackfillsSuppliedMetadataWithoutCrawlingArticleHost()
    {
        await using var env = await Fixture.Create(Feed(4).Replace(Site, "https://publisher.example"));
        env.Source.BackfillLimit = 2;
        var first = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal(2, first.Items.Count);
        Assert.All(first.Items, item =>
        {
            Assert.StartsWith("https://publisher.example/post-", item.CanonicalUrl);
            Assert.StartsWith("Supplied body", item.Body);
            Assert.Null(item.PublishedAt);
        });
        await env.Commit(first);
        var second = await env.Engine.FetchAsync(env.Source, default);
        Assert.EndsWith("/post-3", Assert.Single(second.Items).CanonicalUrl);
        await env.Commit(second);
        Assert.Empty((await env.Engine.FetchAsync(env.Source, default)).Items);
        Assert.All(env.Fetcher.Requests, request => Assert.Equal("example.org", request.Url.Host));
    }

    [Fact]
    public async Task UnavailableRobotsAndArchiveRepresentationsDoNotDiscardUsableFeedEntries()
    {
        await using var env = await Fixture.Create(Feed(4));
        env.Source.BackfillLimit = 2;
        foreach (var path in new[] { "/robots.txt", "/sitemap.xml", "/" })
            env.Fetcher.Reject(path, new RemoteFetchException("Remote source returned HTTP 406."));
        var first = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal(2, first.Items.Count);
        await env.Commit(first);
        var second = await env.Engine.FetchAsync(env.Source, default);
        Assert.EndsWith("/post-3", Assert.Single(second.Items).CanonicalUrl);
        Assert.Contains("406", second.Message);
        await env.Commit(second);
        var third = await env.Engine.FetchAsync(env.Source, default);
        Assert.Empty(third.Items);
        Assert.Equal("limited", third.Status);
        Assert.Equal(1, env.Fetcher.Requests.Count(request => request.Url.AbsolutePath == "/sitemap.xml"));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task RobotsAccessAndTransientErrorsStillStopDiscovery(int status)
    {
        await using var env = await Fixture.Create(Feed(4));
        env.Fetcher.Reject("/robots.txt", new RemoteFetchException($"Remote source returned HTTP {status}."));
        await Assert.ThrowsAsync<RemoteFetchException>(() => env.Engine.FetchAsync(env.Source, default));
        Assert.DoesNotContain(env.Fetcher.Requests, request => request.Url.AbsolutePath == "/feed.xml");
    }

    [Fact]
    public async Task SuppliedOffsiteItemsStillRejectPrivateCredentialAndSensitiveUrls()
    {
        var blocked = new[] { "http://127.0.0.1/private", "https://localhost/private", "https://publisher.example:8080/private",
            "https://user:secret@publisher.example/private", "https://publisher.example/private?api_key=secret" };
        var entries = string.Concat(blocked.Select((url, index) => $"<item><title>Private fixture {index}</title><link>{System.Net.WebUtility.HtmlEncode(url)}</link></item>"));
        entries += "<item><title>Public fixture</title><link>https://publisher.example/public</link></item>";
        await using var env = await Fixture.Create(Feed().Replace("</channel>", entries + "</channel>"));
        env.Source.BackfillLimit = 1;
        var batch = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal("https://publisher.example/public", Assert.Single(batch.Items).CanonicalUrl);
        Assert.All(env.Fetcher.Requests, request => Assert.Equal("example.org", request.Url.Host));
    }

    [Fact]
    public async Task RefusedFeedDoesNotBecomeASuccessfulEmptyArchive()
    {
        await using var env = await Fixture.Create(Feed(4));
        env.Fetcher.Reject("/feed.xml", new RemoteFetchException("Remote source returned HTTP 406."));
        var batch = await env.Engine.FetchAsync(env.Source, default);
        Assert.Empty(batch.Items);
        Assert.Equal("unavailable", batch.Status);
        Assert.Contains("406", batch.Message);
        Assert.DoesNotContain(env.Fetcher.Requests, request => request.Url.AbsolutePath == "/sitemap.xml");
    }

    [Fact]
    public async Task AtomAdvertisedArchiveKeepsSuppliedBodyAndNextPageRemainder()
    {
        await using var env = await Fixture.Create("<feed xmlns='http://www.w3.org/2005/Atom'><link rel='next' href='/old-feed.xml'/></feed>");
        env.Source.BackfillLimit = 1;
        env.Fetcher.Put("/old-feed.xml", "<feed xmlns='http://www.w3.org/2005/Atom'><entry><id>old-a</id><title>A</title><link href='https://example.org/old-a'/><content>Original A</content></entry><entry><id>old-b</id><title>B</title><link href='https://example.org/old-b'/><content>Original B</content></entry></feed>");
        var first = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal("Original A", Assert.Single(first.Items).Body);
        await env.Commit(first);
        var second = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal("Original B", Assert.Single(second.Items).Body);
        Assert.DoesNotContain(env.Fetcher.Requests, r => r.Url.AbsolutePath is "/old-a" or "/old-b");
    }

    [Fact]
    public async Task AdvertisedBlogHomeRemapsLegacyFeedDirectoryAndKeepsArchiveCursor()
    {
        await using var env = await Fixture.Create();
        env.Source.Url = Site + "/blog/rss.xml";
        env.Source.BackfillLimit = 1;
        env.Fetcher.Put("/blog/rss.xml", "<rss><channel><link>https://example.org/discover/blog/</link></channel></rss>");
        env.Fetcher.Put("/discover/blog/", "<h2><a href='/discover/blog/first'>First</a></h2><h2><a href='/discover/blog/second'>Second</a></h2>", "text/html");
        env.Fetcher.Put("/discover/blog/first", "<title>First</title>", "text/html");
        env.Fetcher.Put("/discover/blog/second", "<title>Second</title>", "text/html");
        var first = await env.Engine.FetchAsync(env.Source, default);
        Assert.EndsWith("/first", Assert.Single(first.Items).CanonicalUrl);
        await env.Commit(first);
        var second = await env.Engine.FetchAsync(env.Source, default);
        Assert.EndsWith("/second", Assert.Single(second.Items).CanonicalUrl);
        Assert.Equal(1, env.Fetcher.Requests.Count(r => r.Url.AbsolutePath == "/sitemap.xml"));
    }

    [Fact]
    public async Task CategoryFeedNeverWidensArchiveCandidatesToWholePublisher()
    {
        await using var env = await Fixture.Create();
        env.Source.Url = Site + "/category/specific/feed/";
        env.Source.BackfillUrl = Site + "/map.xml";
        env.Fetcher.Put("/category/specific/feed/", Feed());
        env.Fetcher.Put("/map.xml", Sitemap([Site + "/outside-topic", Site + "/category/specific/post"]));
        env.Fetcher.Put("/outside-topic", "<title>Outside topic</title>", "text/html");
        env.Fetcher.Put("/category/specific/post", "<title>Chosen topic</title>", "text/html");
        // Category URLs are navigation-ambiguous, so without actual feed membership this
        // subscription honestly declines to infer that an arbitrary sitemap URL is a post.
        Assert.Empty((await env.Engine.FetchAsync(env.Source, default)).Items);
        Assert.DoesNotContain(env.Fetcher.Requests, r => r.Url.AbsolutePath == "/outside-topic");
    }

    [Fact]
    public async Task SitemapLastmodIsNeverPublicationAndNavigationUrlsAreSkipped()
    {
        await using var env = await Fixture.Create();
        env.Fetcher.Put("/robots.txt", "User-agent: *\nSitemap: https://example.org/map.xml", "text/plain");
        env.Fetcher.Put("/map.xml", Sitemap([Site + "/about", Site + "/archive", Site + "/p/real-post"]));
        env.Fetcher.Put("/p/real-post", "<title>Original publisher title</title><meta name='description' content='Actual excerpt'>", "text/html");
        var item = Assert.Single((await env.Engine.FetchAsync(env.Source, default)).Items);
        Assert.Equal(Site + "/p/real-post", item.CanonicalUrl);
        Assert.Null(item.PublishedAt);
        Assert.Null(item.Body);
        Assert.Contains("map.xml", item.MetadataJson);
        Assert.DoesNotContain(env.Fetcher.Requests, r => r.Url.AbsolutePath == "/about");
    }

    [Fact]
    public async Task LargeSitemapIndexRotatesChildrenWithoutDiscardingUnqueuedEntries()
    {
        await using var env = await Fixture.Create();
        env.Source.BackfillLimit = 1;
        env.Source.BackfillUrl = Site + "/index.xml";
        env.Fetcher.Put("/index.xml", "<sitemapindex>" + string.Concat(Enumerable.Range(1, 40).Select(i => $"<sitemap><loc>{Site}/map-{i}.xml</loc></sitemap>")) + "</sitemapindex>");
        for (var i = 1; i <= 40; i++) env.Fetcher.Put($"/map-{i}.xml", Sitemap([Site + $"/old-{i}"]));
        env.Fetcher.ArticleFallback = true;
        var urls = new HashSet<string>();
        for (var day = 0; day < 40; day++)
        {
            var batch = await env.Engine.FetchAsync(env.Source, default);
            Assert.Single(batch.Items);
            Assert.True(urls.Add(batch.Items[0].CanonicalUrl));
            Assert.True(JsonDocument.Parse(batch.StateJson!).RootElement.GetProperty("Pages").GetArrayLength() <= 32);
            await env.Commit(batch);
        }
        Assert.Equal(40, urls.Count);
        Assert.Contains(Site + "/old-40", urls);
    }

    [Fact]
    public async Task NestedSitemapIndexesDrainWithoutStallingOrDroppingQueuedChildren()
    {
        await using var env = await Fixture.Create();
        env.Source.BackfillLimit = 1;
        env.Source.BackfillUrl = Site + "/index.xml";
        env.Fetcher.Put("/index.xml", "<sitemapindex>" + string.Concat(Enumerable.Range(1, 40)
            .Select(i => $"<sitemap><loc>{Site}/nested-{i}.xml</loc></sitemap>")) + "</sitemapindex>");
        for (var i = 1; i <= 40; i++)
        {
            env.Fetcher.Put($"/nested-{i}.xml", $"<sitemapindex><sitemap><loc>{Site}/map-{i}.xml</loc></sitemap></sitemapindex>");
            env.Fetcher.Put($"/map-{i}.xml", Sitemap([Site + $"/old-{i}"]));
        }
        env.Fetcher.ArticleFallback = true;
        var urls = new HashSet<string>();
        for (var day = 0; day < 80 && urls.Count < 40; day++)
        {
            // Recreate the adapter each day to exercise only persisted progress.
            var batch = await new RssArchiveBackfill(env.Db, env.Fetcher).FetchAsync(env.Source, default);
            foreach (var item in batch.Items) Assert.True(urls.Add(item.CanonicalUrl));
            using var state = JsonDocument.Parse(batch.StateJson!);
            Assert.InRange(state.RootElement.GetProperty("Pages").GetArrayLength(), 0, 32);
            await env.Commit(batch);
        }
        Assert.Equal(40, urls.Count);
        Assert.Contains(Site + "/old-1", urls);
        Assert.Contains(Site + "/old-40", urls);
    }

    [Fact]
    public async Task PreviouslyFullNestedSitemapCursorRecoversWithoutDuplicatingStoredItems()
    {
        await using var env = await Fixture.Create();
        env.Source.BackfillLimit = 1;
        env.Source.BackfillUrl = Site + "/index.xml";
        env.Fetcher.Put("/index.xml", "<sitemapindex>" + string.Concat(Enumerable.Range(1, 40)
            .Select(i => $"<sitemap><loc>{Site}/nested-{i}.xml</loc></sitemap>")) + "</sitemapindex>");
        for (var i = 1; i <= 40; i++)
        {
            env.Fetcher.Put($"/nested-{i}.xml", $"<sitemapindex><sitemap><loc>{Site}/map-{i}.xml</loc></sitemap></sitemapindex>");
            env.Fetcher.Put($"/map-{i}.xml", Sitemap([Site + $"/old-{i}"]));
        }
        env.Fetcher.ArticleFallback = true;
        var (known, _) = await env.Ingestion.UpsertAsync(new NormalizedItem
        { CanonicalUrl = Site + "/old-1", Title = "TEST FIXTURE retained article" }, env.Source.Id, [], default);
        known.IsPublic = false;
        env.Db.States.Add(new ItemState { ItemId = known.Id, Saved = true, Read = true });
        env.Source.BackfillStateJson = JsonSerializer.Serialize(new
        {
            Scope = Site + "/",
            Archive = env.Source.BackfillUrl,
            Started = true,
            Pages = Enumerable.Range(1, 31).Select(i => new { Url = Site + $"/nested-{i}.xml", Offset = 0, Depth = 1 })
                .Append(new { Url = Site + "/index.xml", Offset = 31, Depth = 0 }),
            Visited = Array.Empty<string>()
        });
        await env.Db.SaveChangesAsync();
        for (var day = 0; day < 80 && await env.Db.Items.CountAsync() < 40; day++)
            await env.Commit(await new RssArchiveBackfill(env.Db, env.Fetcher).FetchAsync(env.Source, default));
        Assert.Equal(40, await env.Db.Items.CountAsync());
        await env.Db.Entry(known).ReloadAsync();
        Assert.False(known.IsPublic);
        Assert.Equal("TEST FIXTURE retained article", known.Title);
        Assert.True((await env.Db.States.SingleAsync()).Saved);
        Assert.True((await env.Db.States.SingleAsync()).Read);
    }

    [Fact]
    public async Task HttpFeedAndRobotsUpgradeToHttpsAndArchiveProgressSurvivesReload()
    {
        await using var env = await Fixture.Create(Feed(2));
        env.Source.Url = "http://example.org/feed.xml";
        env.Source.BackfillUrl = "http://example.org/map.xml";
        env.Source.BackfillLimit = 2;
        // The fixture models final HTTPS URLs after ordinary safe-fetcher redirects.
        env.Fetcher.Put("/robots.txt", "User-agent: *\nAllow: /", "text/plain");
        env.Fetcher.Put("/map.xml", Sitemap([Site + "/old-1", Site + "/old-2"]));
        env.Fetcher.ArticleFallback = true;
        var first = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal(new[] { Site + "/post-1", Site + "/old-1" }, first.Items.Select(i => i.CanonicalUrl));
        await env.Commit(first);
        var second = await new RssArchiveBackfill(env.Db, env.Fetcher).FetchAsync(env.Source, default);
        Assert.Equal(Site + "/old-2", Assert.Single(second.Items).CanonicalUrl);
        Assert.Contains(env.Fetcher.Requests, r => r.Url == new Uri("http://example.org/robots.txt"));
        Assert.Contains(env.Fetcher.Requests, r => r.Url == new Uri(Site + "/robots.txt"));
    }

    [Theory]
    [InlineData("https://example.org/feed.xml", "http://example.org/feed.xml")]
    [InlineData("http://example.org/feed.xml", "https://elsewhere.example/feed.xml")]
    [InlineData("http://example.org/feed.xml", "https://example.org:444/feed.xml")]
    public async Task FeedRedirectStillRejectsDowngradesOffsiteHostsAndNonstandardPorts(string original, string final)
    {
        await using var env = await Fixture.Create();
        env.Source.Url = original;
        env.Fetcher.Put("/feed.xml", Feed(2), final: final);
        var batch = await env.Engine.FetchAsync(env.Source, default);
        Assert.Empty(batch.Items);
        Assert.Equal("unavailable", batch.Status);
    }

    [Fact]
    public async Task HttpSubscriptionDoesNotPermitHttpsArchiveOrRobotsDowngrades()
    {
        await using var env = await Fixture.Create();
        env.Source.Url = "http://example.org/feed.xml";
        env.Source.BackfillUrl = Site + "/map.xml";
        env.Fetcher.Put("/robots.txt", "User-agent: *\nAllow: /", "text/plain");
        env.Fetcher.Put("/map.xml", Sitemap([Site + "/old-1"]), final: "http://example.org/map.xml");
        env.Fetcher.ArticleFallback = true;
        Assert.Empty((await env.Engine.FetchAsync(env.Source, default)).Items);
        Assert.DoesNotContain(env.Fetcher.Requests, r => r.Url.AbsolutePath == "/old-1");
        env.Fetcher.Put("/robots.txt", "User-agent: *\nAllow: /", "text/plain", "http://example.org/robots.txt");
        var refused = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal("unavailable", refused.Status);
        Assert.Empty(refused.Items);
    }

    [Fact]
    public async Task CandidateScopePrivateUrlsRobotsAndOffsiteRedirectsAreRespected()
    {
        await using var env = await Fixture.Create(Feed(1, "/blog"));
        env.Source.Url = Site + "/blog/feed.xml";
        env.Source.BackfillUrl = Site + "/map.xml";
        env.Fetcher.Put("/blog/feed.xml", Feed(1, "/blog"));
        env.Fetcher.Put("/robots.txt", "User-agent: *\nDisallow: /blog/disallowed", "text/plain");
        env.Fetcher.Put("/map.xml", Sitemap(["http://127.0.0.1/private", "https://evil.example/blog/secret", Site + "/outside-scope", Site + "/blog/disallowed", Site + "/blog/redirect", Site + "/blog/valid"]));
        env.Fetcher.Put("/blog/redirect", "<title>Off-site text</title>", "text/html", "https://evil.example/redirected");
        env.Fetcher.Put("/blog/valid", "<title>Valid article</title>", "text/html");
        var item = Assert.Single((await env.Engine.FetchAsync(env.Source, default)).Items);
        Assert.EndsWith("/blog/valid", item.CanonicalUrl);
        Assert.DoesNotContain(env.Fetcher.Requests, r => r.Url.Host != "example.org" || r.Url.AbsolutePath is "/outside-scope" or "/blog/disallowed");
    }

    [Fact]
    public async Task MissingArchiveIsHonestAndTransientFailureDoesNotMutateDurableCursor()
    {
        await using var env = await Fixture.Create();
        env.Source.BackfillUrl = Site + "/missing.xml";
        var missing = await env.Engine.FetchAsync(env.Source, default);
        Assert.Empty(missing.Items);
        Assert.Equal("limited", missing.Status);
        Assert.Contains("404", missing.Message);
        env.Source.BackfillStateJson = missing.StateJson;
        var requestCount = env.Fetcher.Requests.Count(r => r.Url.AbsolutePath == "/missing.xml");
        var waiting = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal("limited", waiting.Status);
        Assert.Equal(requestCount, env.Fetcher.Requests.Count(r => r.Url.AbsolutePath == "/missing.xml"));
        var before = env.Source.BackfillStateJson;
        env.Fetcher.Failure = new RemoteFetchException("Remote source returned HTTP 503.");
        await Assert.ThrowsAsync<RemoteFetchException>(() => env.Engine.FetchAsync(env.Source, default));
        Assert.Equal(before, env.Source.BackfillStateJson);
    }

    [Theory]
    [InlineData("https://example.org/feed.xml?token=private")]
    [InlineData("https://example.org/feed.xml?api_key=private")]
    public async Task PrivateFeedDoesNotDiscoverPublicArchive(string url)
    {
        await using var env = await Fixture.Create();
        env.Source.Url = url;
        var batch = await env.Engine.FetchAsync(env.Source, default);
        Assert.Equal("unsupported", batch.Status);
        Assert.Empty(env.Fetcher.Requests);
    }

    [Fact]
    public async Task DtdAndMalformedArchiveAreRejected()
    {
        await using var env = await Fixture.Create();
        env.Source.BackfillUrl = Site + "/map.xml";
        env.Fetcher.Put("/map.xml", "<!DOCTYPE urlset [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><urlset>&x;</urlset>");
        await Assert.ThrowsAsync<System.Xml.XmlException>(() => env.Engine.FetchAsync(env.Source, default));
    }

    [Fact]
    public async Task OversizeArticleIsSkippedAndCursorContinuesToNextRealArticle()
    {
        await using var env = await Fixture.Create();
        env.Source.BackfillUrl = Site + "/map.xml";
        env.Source.BackfillLimit = 1;
        env.Fetcher.Put("/map.xml", Sitemap([Site + "/too-large", Site + "/valid-next", Site + "/valid-last"]));
        env.Fetcher.Put("/too-large", "<title>Too large</title>" + new string('x', 1_500_001), "text/html");
        env.Fetcher.Put("/valid-next", "<title>Original next article</title>" + new string('x', 620_000), "text/html");
        env.Fetcher.Put("/valid-last", "<title>Original last article</title>", "text/html");
        var first = await env.Engine.FetchAsync(env.Source, default);
        var item = Assert.Single(first.Items);
        Assert.EndsWith("/valid-next", item.CanonicalUrl);
        Assert.Null(item.Body);
        Assert.Null(item.PublishedAt);
        Assert.Contains("1.5 MB", first.Message);
        Assert.Equal(2, JsonDocument.Parse(first.StateJson!).RootElement.GetProperty("Pages")[0].GetProperty("Offset").GetInt32());
        await env.Commit(first);
        var second = await env.Engine.FetchAsync(env.Source, default);
        Assert.EndsWith("/valid-last", Assert.Single(second.Items).CanonicalUrl);
        Assert.Equal(1, env.Fetcher.Requests.Count(r => r.Url.AbsolutePath == "/too-large"));
        Assert.All(env.Fetcher.Requests.Where(r => r.Url.AbsolutePath is "/too-large" or "/valid-next"), r => Assert.Equal(1_500_000, r.MaxBytes));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(410)]
    public async Task PermissionLimitedOrMissingArticleDoesNotBlockFollowingCandidate(int status)
    {
        await using var env = await Fixture.Create();
        env.Source.BackfillUrl = Site + "/map.xml";
        env.Source.BackfillLimit = 1;
        env.Fetcher.Put("/map.xml", Sitemap([Site + "/unreadable", Site + "/valid"]));
        env.Fetcher.Reject("/unreadable", new RemoteFetchException($"Remote source returned HTTP {status}."));
        env.Fetcher.Put("/valid", "<title>Original valid article</title>", "text/html");
        var batch = await env.Engine.FetchAsync(env.Source, default);
        Assert.EndsWith("/valid", Assert.Single(batch.Items).CanonicalUrl);
        Assert.Contains("skipped", batch.Message);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task ThrottledOrTransientArticleFailureRetainsPriorDurableCursor(int status)
    {
        await using var env = await Fixture.Create();
        env.Source.BackfillUrl = Site + "/map.xml";
        env.Fetcher.Put("/map.xml", Sitemap([Site + "/retry-later", Site + "/valid"]));
        env.Fetcher.Reject("/retry-later", new RemoteFetchException($"Remote source returned HTTP {status}."));
        env.Fetcher.Put("/valid", "<title>Original valid article</title>", "text/html");
        var before = env.Source.BackfillStateJson;
        var error = await Assert.ThrowsAsync<RemoteFetchException>(() => env.Engine.FetchAsync(env.Source, default));
        Assert.Contains(status.ToString(), error.Message);
        Assert.Equal(before, env.Source.BackfillStateJson);
        Assert.DoesNotContain(env.Fetcher.Requests, r => r.Url.AbsolutePath == "/valid");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required AppDbContext Db { get; init; }
        public required IngestionService Ingestion { get; init; }
        public required RssArchiveBackfill Engine { get; init; }
        public required RoutedFetcher Fetcher { get; init; }
        public required Source Source { get; init; }
        public static async Task<Fixture> Create(string? feed = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var fetcher = new RoutedFetcher();
            fetcher.Put("/feed.xml", feed ?? Feed());
            var ingestion = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
            var source = new Source { Name = "Archive fixture", Kind = "rss", Url = Site + "/feed.xml", ResultLimit = 1, BackfillLimit = 3 };
            db.Sources.Add(source);
            await db.SaveChangesAsync();
            return new Fixture { Connection = connection, Db = db, Ingestion = ingestion, Engine = new RssArchiveBackfill(db, fetcher), Fetcher = fetcher, Source = source };
        }
        public async Task Commit(BackfillBatch batch)
        {
            foreach (var item in batch.Items) await Ingestion.UpsertAsync(item, Source.Id, [], default);
            Source.BackfillStateJson = batch.StateJson;
            await Db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    private sealed class RoutedFetcher : IRemoteFetcher
    {
        private readonly Dictionary<string, RemoteResponse> responses = [];
        private readonly Dictionary<string, Exception> failures = [];
        public List<RemoteRequest> Requests { get; } = [];
        public bool ArticleFallback { get; set; }
        public Exception? Failure { get; set; }
        public void Put(string path, string body, string mime = "application/xml", string? final = null) => responses[path] =
            new RemoteResponse(200, new Uri(final ?? Site + path), mime, Encoding.UTF8.GetBytes(body), null, null);
        public void Reject(string path, Exception error) => failures[path] = error;
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            if (failures.TryGetValue(request.Url.AbsolutePath, out var error)) throw error;
            if (responses.TryGetValue(request.Url.AbsolutePath, out var response))
            {
                if (response.Body.Length > request.MaxBytes) throw new RemoteFetchException("Remote response exceeds the size limit.");
                return Task.FromResult(response);
            }
            if (ArticleFallback && request.Url.AbsolutePath.StartsWith("/old-"))
                return Task.FromResult(new RemoteResponse(200, request.Url, "text/html", Encoding.UTF8.GetBytes("<title>Original " + request.Url.AbsolutePath + "</title>"), null, null));
            throw new RemoteFetchException("Remote source returned HTTP 404.");
        }
    }
}
