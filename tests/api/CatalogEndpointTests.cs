using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class CatalogEndpointTests
{
    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"rabbithole-catalog-{Guid.NewGuid():N}.db");
        public Func<RemoteRequest, RemoteResponse> Fetch { get; set; } = request => throw new InvalidOperationException("Unexpected fixture fetch: " + request.Url.Host);
        public Action<HttpContext>? ObserveRequest { get; set; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("ConnectionStrings:Default", "Data Source=" + _path);
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
            builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(IngestionWorker)).ToList())
                    services.Remove(descriptor);
                services.RemoveAll<IRemoteFetcher>();
                services.AddSingleton<IRemoteFetcher>(new FixtureFetcher(this));
                services.AddSingleton<IStartupFilter>(new RequestObserver(this));
            });
        }
        private sealed class RequestObserver(ApiFactory factory) : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use(async (context, continuation) =>
                {
                    factory.ObserveRequest?.Invoke(context);
                    await continuation();
                });
                next(app);
            };
        }
        private sealed class FixtureFetcher(ApiFactory factory) : IRemoteFetcher
        {
            public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken) => Task.FromResult(factory.Fetch(request));
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                foreach (var path in new[] { _path, _path + "-wal", _path + "-shm" })
                    try { File.Delete(path); } catch (IOException) { }
        }
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {text}");
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
    }
    private static NormalizedItem Record(string url, string? id = null, string provider = "rss", string title = "TEST FIXTURE catalog item") =>
        new() { CanonicalUrl = url, Provider = provider, ProviderId = id, Title = title, Body = "Supplied fixture source text.", TextProvenance = "feed-content" };

    [Fact]
    public async Task ExportRedactsCredentialsInItemsIdentitiesAndSourceOriginsWithoutMutatingDatabase()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        const string secret = "TEST_SECRET_DO_NOT_EXPORT";
        var privateUrl = "https://soundcloud.com/test/track?secret_token=" + secret + "&color=blue";
        var embed = "https://w.soundcloud.com/player/?url=" + Uri.EscapeDataString(privateUrl) + "&auto_play=false";
        var metadata = JsonSerializer.Serialize(new
        {
            providerReferences = new[] { new { provider = "soundcloud", id = privateUrl } },
            enclosureUrl = "https://example.org/audio?access_token=" + secret,
            player = embed,
            venue = "TEST FIXTURE venue"
        });
        Guid itemId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = new Source { Name = "TEST FIXTURE origin", Url = "https://example.org/feed?api_key=" + secret, Enabled = false };
            var item = new ContentItem
            {
                SourceId = source.Id,
                Kind = "audio",
                Title = "TEST FIXTURE private track",
                CanonicalUrl = privateUrl,
                Provider = "soundcloud",
                ProviderId = privateUrl,
                EmbedUrl = embed,
                MetadataJson = metadata
            };
            itemId = item.Id;
            db.AddRange(source, item);
            db.ItemIdentities.Add(new ItemIdentity { Key = new string('A', 64), ItemId = item.Id, Kind = "provider:soundcloud", Scope = privateUrl, Value = privateUrl });
            db.ItemSources.Add(new ItemSource { ItemId = item.Id, SourceId = source.Id, SourceKey = source.Id, Name = source.Name, Kind = source.Kind, Url = source.Url });
            await db.SaveChangesAsync();
        }
        var export = await Read(await client.PostAsync("/api/export", null));
        Assert.DoesNotContain(secret, export.GetRawText());
        Assert.Single(export.GetProperty("identities").EnumerateArray());
        Assert.Single(export.GetProperty("itemSources").EnumerateArray());
        var exportedItem = Assert.Single(export.GetProperty("items").EnumerateArray());
        Assert.Contains("color=blue", exportedItem.GetProperty("canonicalUrl").GetString());
        using var exportedMetadata = JsonDocument.Parse(exportedItem.GetProperty("metadataJson").GetString()!);
        Assert.Equal("TEST FIXTURE venue", exportedMetadata.RootElement.GetProperty("venue").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.Items.SingleAsync(x => x.Id == itemId);
            Assert.Equal(privateUrl, item.CanonicalUrl); Assert.Equal(privateUrl, item.ProviderId);
            Assert.Equal(embed, item.EmbedUrl); Assert.Equal(metadata, item.MetadataJson);
            var identity = await db.ItemIdentities.SingleAsync();
            Assert.Equal(privateUrl, identity.Scope); Assert.Equal(privateUrl, identity.Value);
            Assert.Contains(secret, (await db.ItemSources.SingleAsync()).Url);
            Assert.Contains(secret, (await db.Sources.SingleAsync()).Url);
        }
    }

    [Fact]
    public async Task MergedOldIdRoutesResolveSurvivorWhileFeedHistoryRetainsBothEntries()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        Guid winnerId, oldId, relatedId, editionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ingestion = scope.ServiceProvider.GetRequiredService<IngestionService>();
            var topic = new Topic { Name = "TEST FIXTURE quartz", Keywords = "quartz" };
            db.Topics.Add(topic); await db.SaveChangesAsync();
            var winner = (await ingestion.UpsertAsync(Record("https://example.org/quartz", title: "TEST FIXTURE quartz original"), null, [], default)).Item;
            winner.IngestedAt = DateTimeOffset.UtcNow.AddHours(-2); await db.SaveChangesAsync();
            var duplicateInput = Record("https://openalex.org/W4242", "W4242", "openalex", "TEST FIXTURE quartz duplicate");
            duplicateInput.Doi = "10.1234/catalog-fixture";
            var duplicate = (await ingestion.UpsertAsync(duplicateInput, null, [], default)).Item;
            var related = (await ingestion.UpsertAsync(Record("https://example.org/quartz-related", title: "TEST FIXTURE quartz related"), null, [], default)).Item;
            winnerId = winner.Id; oldId = duplicate.Id; relatedId = related.Id;
            db.Notes.Add(new Note { ItemId = oldId, Body = "TEST FIXTURE note before merge" });
            var edition = new FeedEdition { Day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd") };
            editionId = edition.Id; db.Editions.Add(edition);
            db.Entries.AddRange(new FeedEntry { EditionId = edition.Id, ItemId = oldId, Position = 0, Reason = "TEST FIXTURE earlier selection" },
                new FeedEntry { EditionId = edition.Id, ItemId = winnerId, Position = 1, Reason = "TEST FIXTURE other selection" });
            await db.SaveChangesAsync();
            var bridge = Record(winner.CanonicalUrl, "2401.00001", "arxiv", "TEST FIXTURE quartz joined paper");
            bridge.Doi = duplicateInput.Doi; bridge.ArxivId = "2401.00001";
            Assert.Equal(winnerId, (await ingestion.UpsertAsync(bridge, null, [], default)).Item.Id);
        }
        var item = await Read(await client.GetAsync($"/api/items/{oldId}"));
        Assert.Equal(winnerId, item.GetProperty("id").GetGuid());
        Assert.Equal("TEST FIXTURE note before merge", Assert.Single(item.GetProperty("notes").EnumerateArray()).GetProperty("body").GetString());
        var state = await Read(await client.PatchAsJsonAsync($"/api/items/{oldId}/state", new { saved = true }));
        Assert.Equal(winnerId, state.GetProperty("itemId").GetGuid()); Assert.True(state.GetProperty("saved").GetBoolean());
        var note = await Read(await client.PostAsJsonAsync($"/api/items/{oldId}/notes", new { body = "TEST FIXTURE note after merge" }));
        Assert.Equal(winnerId, note.GetProperty("itemId").GetGuid());
        var relatedItems = await Read(await client.GetAsync($"/api/items/{oldId}/related"));
        Assert.Contains(relatedItems.EnumerateArray(), x => x.GetProperty("id").GetGuid() == relatedId);
        Assert.DoesNotContain(relatedItems.EnumerateArray(), x => x.GetProperty("id").GetGuid() == oldId);
        var lesson = await Read(await client.GetAsync($"/api/items/{oldId}/lesson"));
        Assert.Equal(winnerId, lesson.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.DoesNotContain(lesson.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == oldId);
        var feed = await Read(await client.GetAsync("/api/discover/today"));
        Assert.Equal(1, feed.GetProperty("total").GetInt32());
        Assert.Equal(winnerId, Assert.Single(feed.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using var verification = factory.Services.CreateScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(winnerId, (await verifyDb.Items.IgnoreQueryFilters().SingleAsync(x => x.Id == oldId)).MergedIntoId);
        var retained = await verifyDb.Entries.Where(x => x.EditionId == editionId).ToListAsync();
        Assert.Equal(2, retained.Count);
        Assert.Contains(retained, x => x.ItemId == oldId); Assert.Contains(retained, x => x.ItemId == winnerId);
        Assert.Equal(2, await verifyDb.Notes.CountAsync(x => x.ItemId == winnerId));
    }

    [Fact]
    public async Task SourceMembershipFiltersAndDiscoveryIncludeEnabledSecondarySourceAfterUrlDeduplication()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        Guid itemId, secondaryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ingestion = scope.ServiceProvider.GetRequiredService<IngestionService>();
            var primary = new Source { Name = "TEST FIXTURE disabled primary", Url = "https://example.org/a.xml", Enabled = false };
            var secondary = new Source { Name = "TEST FIXTURE enabled secondary", Url = "https://example.org/b.xml", Enabled = true };
            db.AddRange(primary, secondary); await db.SaveChangesAsync();
            var first = await ingestion.UpsertAsync(Record("https://example.org/shared", "shared", title: "TEST FIXTURE membershipitem"), primary.Id, [], default);
            var second = await ingestion.UpsertAsync(Record("https://example.org/shared", "different-guid", title: "TEST FIXTURE membershipitem"), secondary.Id, [], default);
            Assert.Equal(first.Item.Id, second.Item.Id); Assert.False(second.Created);
            Assert.Equal(primary.Id, second.Item.SourceId);
            itemId = first.Item.Id; secondaryId = secondary.Id;
        }
        foreach (var route in new[] { $"/api/items?sourceId={secondaryId}", $"/api/search?q=membershipitem&sourceId={secondaryId}", "/api/discover/today" })
        {
            var result = await Read(await client.GetAsync(route));
            Assert.Equal(1, result.GetProperty("total").GetInt32());
            var item = Assert.Single(result.GetProperty("items").EnumerateArray());
            Assert.Equal(itemId, item.GetProperty("id").GetGuid());
            Assert.Equal(2, item.GetProperty("sources").GetArrayLength());
        }
    }

    [Fact]
    public async Task ExplicitEmptyTopicSelectionSurvivesRealRefreshAndKeywordEdits()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        Guid sourceId, topicId, itemId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var topic = new Topic { Name = "TEST FIXTURE optout", Keywords = "quartz" };
            var source = new Source { Name = "TEST FIXTURE assigned source", Url = "https://example.org/fixture.xml", Enabled = false, TopicIds = [topic.Id] };
            db.AddRange(topic, source); await db.SaveChangesAsync();
            sourceId = source.Id; topicId = topic.Id;
            var item = (await scope.ServiceProvider.GetRequiredService<IngestionService>().UpsertAsync(
                Record("https://example.org/fixture-article", "fixture-guid", title: "TEST FIXTURE quartz"), sourceId, [topicId], default)).Item;
            itemId = item.Id; Assert.Contains(topicId, item.TopicIds);
        }
        var patched = await Read(await client.PatchAsJsonAsync($"/api/items/{itemId}", new { topicIds = Array.Empty<Guid>() }));
        Assert.Empty(patched.GetProperty("topicIds").EnumerateArray());
        factory.Fetch = request => new RemoteResponse(200, request.Url, "application/rss+xml", Encoding.UTF8.GetBytes("""
            <rss version="2.0"><channel><item><guid>fixture-guid</guid><title>TEST FIXTURE quartz refreshed</title>
            <link>https://example.org/fixture-article</link><description>Source refreshed excerpt</description></item></channel></rss>
            """), null, null);
        var refreshed = await Read(await client.PostAsync($"/api/sources/{sourceId}/refresh", null));
        Assert.Equal("completed", refreshed.GetProperty("status").GetString());
        await Read(await client.PatchAsJsonAsync($"/api/topics/{topicId}", new { keywords = "quartz, refreshed" }));
        var after = await Read(await client.GetAsync($"/api/items/{itemId}"));
        Assert.Contains("refreshed", after.GetProperty("title").GetString());
        Assert.Empty(after.GetProperty("topicIds").EnumerateArray());
        using var verification = factory.Services.CreateScope();
        var edge = await verification.ServiceProvider.GetRequiredService<AppDbContext>().ItemTopics.SingleAsync(x => x.ItemId == itemId && x.TopicId == topicId);
        Assert.False(edge.UserOverride); Assert.True(edge.SourceAssigned); Assert.True(edge.KeywordMatched);
    }

    [Fact]
    public async Task EditingTopicKeywordsReclassifiesExistingCatalogWithoutAnotherImport()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var topic = await Read(await client.PostAsJsonAsync("/api/topics", new { name = "TEST FIXTURE editable topic", keywords = "nonmatchingterm" }));
        var topicId = topic.GetProperty("id").GetGuid();
        Guid itemId;
        using (var scope = factory.Services.CreateScope())
        {
            var item = (await scope.ServiceProvider.GetRequiredService<IngestionService>().UpsertAsync(
                Record("https://example.org/reclassify", title: "TEST FIXTURE zirconium"), null, [], default)).Item;
            itemId = item.Id; Assert.DoesNotContain(topicId, item.TopicIds);
        }
        await Read(await client.PatchAsJsonAsync($"/api/topics/{topicId}", new { keywords = "zirconium" }));
        var matched = await Read(await client.GetAsync($"/api/items/{itemId}"));
        Assert.Contains(matched.GetProperty("topicIds").EnumerateArray(), x => x.GetGuid() == topicId);
        await Read(await client.PatchAsJsonAsync($"/api/topics/{topicId}", new { keywords = "nonmatchingterm" }));
        var removed = await Read(await client.GetAsync($"/api/items/{itemId}"));
        Assert.DoesNotContain(removed.GetProperty("topicIds").EnumerateArray(), x => x.GetGuid() == topicId);
    }

    [Fact]
    public async Task ReplacingFeedUrlKeepsOldGuidRecordTextPersonalStateAndOriginSnapshot()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        const string feedA = "https://example.org/feed-a.xml";
        const string feedB = "https://example.org/feed-b.xml";
        factory.Fetch = request =>
        {
            Assert.Contains(request.Url.AbsoluteUri, new[] { feedA, feedB });
            var suffix = request.Url.AbsoluteUri == feedA ? "a" : "b";
            var xml = $"<rss version='2.0' xmlns:content='http://purl.org/rss/1.0/modules/content/'><channel><item><guid>1</guid><title>TEST FIXTURE feed {suffix}</title><link>https://example.org/article-{suffix}</link><description>Excerpt {suffix}</description><content:encoded>Original source body {suffix}</content:encoded></item></channel></rss>";
            return new RemoteResponse(200, request.Url, "application/rss+xml", Encoding.UTF8.GetBytes(xml), null, null);
        };
        var source = await Read(await client.PostAsJsonAsync("/api/sources", new { name = "TEST FIXTURE changing origin", kind = "rss", url = feedA, enabled = false }));
        var sourceId = source.GetProperty("id").GetGuid();
        Assert.Equal("completed", (await Read(await client.PostAsync($"/api/sources/{sourceId}/refresh", null))).GetProperty("status").GetString());
        var firstList = await Read(await client.GetAsync("/api/items"));
        var oldId = Assert.Single(firstList.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
        await Read(await client.PatchAsJsonAsync($"/api/items/{oldId}/state", new { saved = true, read = true, progress = 0.75 }));
        await Read(await client.PostAsJsonAsync($"/api/items/{oldId}/notes", new { body = "TEST FIXTURE retain original note" }));
        var edited = await Read(await client.PatchAsJsonAsync($"/api/sources/{sourceId}", new { url = feedB }));
        Assert.Equal(1, edited.GetProperty("identityVersion").GetInt32());
        for (var i = 0; i < 2; i++)
        {
            var refreshed = await Read(await client.PostAsync($"/api/sources/{sourceId}/refresh", null));
            Assert.Equal("completed", refreshed.GetProperty("status").GetString());
            Assert.Equal(i == 0 ? 1 : 0, refreshed.GetProperty("imported").GetInt32());
        }
        var oldItem = await Read(await client.GetAsync($"/api/items/{oldId}"));
        Assert.Equal("TEST FIXTURE feed a", oldItem.GetProperty("title").GetString());
        Assert.Equal("Original source body a", oldItem.GetProperty("body").GetString());
        Assert.Equal("https://example.org/article-a", oldItem.GetProperty("canonicalUrl").GetString());
        Assert.True(oldItem.GetProperty("state").GetProperty("saved").GetBoolean());
        Assert.True(oldItem.GetProperty("state").GetProperty("read").GetBoolean());
        Assert.Equal(0.75, oldItem.GetProperty("state").GetProperty("progress").GetDouble());
        Assert.Equal("TEST FIXTURE retain original note", Assert.Single(oldItem.GetProperty("notes").EnumerateArray()).GetProperty("body").GetString());
        var all = await Read(await client.GetAsync("/api/items"));
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        var newItem = Assert.Single(all.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() != oldId);
        Assert.Equal("https://example.org/article-b", newItem.GetProperty("canonicalUrl").GetString());
        using var verification = factory.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        var oldOrigin = await db.ItemSources.SingleAsync(x => x.ItemId == oldId);
        Assert.Equal(feedA, oldOrigin.Url); Assert.Equal(0, oldOrigin.SourceVersion); Assert.Null(oldOrigin.SourceId);
        Assert.Equal(sourceId, oldOrigin.SourceKey);
        var currentOrigin = await db.ItemSources.SingleAsync(x => x.ItemId != oldId);
        Assert.Equal(feedB, currentOrigin.Url); Assert.Equal(1, currentOrigin.SourceVersion); Assert.Equal(sourceId, currentOrigin.SourceId);
        Assert.Equal(2, await db.Items.CountAsync());
    }

    [Fact]
    public async Task SourceEditWaitsForRefreshGateThenResetsOpenAlexCursorAndWindow()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        Guid sourceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = new Source
            {
                Name = "TEST FIXTURE locked watch",
                Kind = "openalex",
                Query = "original query",
                Enabled = false,
                ResearchCursor = "old-pending-cursor",
                ResearchCheckpointAt = DateTimeOffset.UtcNow.AddDays(-2),
                ResearchFromDate = DateTimeOffset.UtcNow.AddDays(-9),
                ResearchWindowStartedAt = DateTimeOffset.UtcNow.AddHours(-1)
            };
            db.Sources.Add(source); await db.SaveChangesAsync(); sourceId = source.Id;
        }
        var requestEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.ObserveRequest = context =>
        {
            if (context.Request.Method == "PATCH" && context.Request.Path == $"/api/sources/{sourceId}") requestEntered.TrySetResult();
        };
        var lease = await IngestionService.AcquireSourceLockAsync(sourceId, default);
        Task<HttpResponseMessage> edit;
        try
        {
            edit = client.PatchAsJsonAsync($"/api/sources/{sourceId}", new { query = "replacement query" });
            await requestEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var winner = await Task.WhenAny(edit, Task.Delay(TimeSpan.FromMilliseconds(150)));
            Assert.NotSame(edit, winner);
            using var heldScope = factory.Services.CreateScope();
            var heldSource = await heldScope.ServiceProvider.GetRequiredService<AppDbContext>().Sources.SingleAsync(x => x.Id == sourceId);
            Assert.Equal("original query", heldSource.Query); Assert.Equal("old-pending-cursor", heldSource.ResearchCursor);
        }
        finally { lease.Dispose(); }
        var result = await Read(await edit.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("replacement query", result.GetProperty("query").GetString());
        Assert.Equal(1, result.GetProperty("identityVersion").GetInt32());
        foreach (var field in new[] { "researchCursor", "researchFromDate", "researchWindowStartedAt", "researchCheckpointAt" })
            Assert.Equal(JsonValueKind.Null, result.GetProperty(field).ValueKind);
        using var verification = factory.Services.CreateScope();
        var persisted = await verification.ServiceProvider.GetRequiredService<AppDbContext>().Sources.SingleAsync(x => x.Id == sourceId);
        Assert.Equal("replacement query", persisted.Query);
        Assert.Null(persisted.ResearchCursor); Assert.Null(persisted.ResearchFromDate);
        Assert.Null(persisted.ResearchWindowStartedAt); Assert.Null(persisted.ResearchCheckpointAt);
    }
}
