using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using Xunit;

namespace RabbitHole.Tests;

public class CoreTests
{
    private sealed class ApiFactory : AuthenticatedApiFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"rabbithole-core-{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("DATABASE_URL", $"Data Source={_path}");
            builder.UseSetting("ConnectionStrings:Default", $"Data Source={_path}");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { try { File.Delete(_path); } catch (IOException) { } }
        }
    }

    [Fact]
    public async Task Item_state_notes_search_edition_holes_and_practice_are_durable()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var topic = (await Read(await client.GetAsync("/api/topics"))).EnumerateArray().First();
        var topicId = topic.GetProperty("id").GetGuid();
        Guid first, second;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            first = Guid.NewGuid(); second = Guid.NewGuid();
            db.Items.AddRange(
                new ContentItem { Id = first, Title = "Consciousness and machines", CanonicalUrl = "https://example.org/real-fixture-a", Provider = "test-fixture", TopicIds = [topicId], Excerpt = "A supplied test record" },
                new ContentItem { Id = second, Title = "Robotics discussion", CanonicalUrl = "https://example.org/real-fixture-b", Provider = "test-fixture", TopicIds = [topicId], Excerpt = "Another supplied test record" });
            await db.SaveChangesAsync();
        }
        var edition1 = await Read(await client.PostAsJsonAsync("/api/discover/today", new { }));
        var edition2 = await Read(await client.GetAsync("/api/discover/today"));
        Assert.Equal(edition1.GetProperty("id").GetGuid(), edition2.GetProperty("id").GetGuid());
        Assert.Equal(edition1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()), edition2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        Assert.Equal(2, edition1.GetProperty("total").GetInt32());

        var state = await Read(await client.PatchAsJsonAsync($"/api/items/{first}/state", new { saved = true, read = true, opened = true, understood = false }));
        Assert.True(state.GetProperty("saved").GetBoolean());
        Assert.True(state.GetProperty("read").GetBoolean());
        var note = await Read(await client.PostAsJsonAsync($"/api/items/{first}/notes", new { body = "My unique psi footnote", quote = "quoted phrase", locator = "page 2" }));
        Assert.Equal(first, note.GetProperty("itemId").GetGuid());
        var search = await Read(await client.GetAsync("/api/search?q=footnote"));
        Assert.Equal(1, search.GetProperty("total").GetInt32());
        var saved = await Read(await client.GetAsync("/api/items?saved=true&notes=true"));
        Assert.Single(saved.GetProperty("items").EnumerateArray());
        Assert.Single(saved.GetProperty("items")[0].GetProperty("notes").EnumerateArray());

        var hole = await Read(await client.PostAsJsonAsync("/api/holes", new { itemId = first }));
        var holeId = hole.GetProperty("id").GetGuid();
        var rootId = hole.GetProperty("nodes")[0].GetProperty("id").GetGuid();
        var branch = await Read(await client.PostAsJsonAsync($"/api/holes/{holeId}/nodes", new { parentId = rootId, itemId = second }));
        Assert.Equal(rootId, branch.GetProperty("parentId").GetGuid());
        Assert.Equal(first, branch.GetProperty("evidenceItemIds")[0].GetGuid());
        var sibling = await Read(await client.PostAsJsonAsync($"/api/holes/{holeId}/nodes", new { parentId = rootId, question = "What other paths connect these ideas?" }));
        Assert.Equal(rootId, sibling.GetProperty("parentId").GetGuid());
        await Read(await client.PostAsJsonAsync($"/api/holes/{holeId}/navigate", new { nodeId = branch.GetProperty("id").GetGuid() }));
        var invalidBranch = await client.PostAsJsonAsync($"/api/holes/{holeId}/nodes", new { parentId = Guid.NewGuid(), itemId = second });
        Assert.Equal(HttpStatusCode.BadRequest, invalidBranch.StatusCode);
        var reloaded = await Read(await client.GetAsync($"/api/holes/{holeId}"));
        Assert.Equal(3, reloaded.GetProperty("nodes").GetArrayLength());
        Assert.Equal(branch.GetProperty("id").GetGuid(), reloaded.GetProperty("lastNodeId").GetGuid());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/holes/{holeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/holes/{holeId}")).StatusCode);

        var defaultSettings = await Read(await client.GetAsync("/api/settings"));
        Assert.False(defaultSettings.GetProperty("practiceEnabled").GetBoolean());
        var enabledSettings = await Read(await client.PatchAsJsonAsync("/api/settings", new { practiceEnabled = true }));
        Assert.True(enabledSettings.GetProperty("practiceEnabled").GetBoolean());
        var prompt = await Read(await client.PostAsJsonAsync("/api/practice/prompts", new { itemId = first }));
        var promptId = prompt.GetProperty("id").GetGuid();
        Assert.Equal(first, prompt.GetProperty("itemId").GetGuid());
        var attempt = await Read(await client.PostAsJsonAsync($"/api/practice/prompts/{promptId}/attempts", new { response = "A considered answer", rating = "good" }));
        Assert.Equal(1, attempt.GetProperty("reviews").GetInt32());
        Assert.Single(attempt.GetProperty("attempts").EnumerateArray());
        Assert.True(attempt.GetProperty("dueAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        var skipped = await Read(await client.PostAsJsonAsync($"/api/practice/prompts/{promptId}/attempts", new { response = "", rating = "skip" }));
        Assert.Equal(1, skipped.GetProperty("reviews").GetInt32());
        var itemAfterPractice = await Read(await client.GetAsync($"/api/items/{first}"));
        Assert.False(itemAfterPractice.GetProperty("state").GetProperty("understood").GetBoolean());
    }

    [Fact]
    public async Task Invalid_references_and_cross_site_mutations_are_rejected()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var missing = await client.PatchAsJsonAsync($"/api/items/{Guid.NewGuid()}/state", new { saved = true });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/topics") { Content = JsonContent.Create(new { name = "test" }) };
        request.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
        var crossSite = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, crossSite.StatusCode);
    }

    [Fact]
    public async Task Ranking_is_repeatable_diverse_and_cites_only_prior_real_items()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var topic = new Topic { Name = "A test interest", Weight = 5 };
        var sourceA = new Source { Name = "Test source A", Url = "https://example.org/a.xml" };
        var sourceB = new Source { Name = "Test source B", Url = "https://example.org/b.xml" };
        db.AddRange(topic, sourceA, sourceB);
        var prior = new ContentItem { Title = "Prior source record", CanonicalUrl = "https://example.org/prior", SourceId = sourceA.Id, TopicIds = [topic.Id] };
        db.Items.Add(prior);
        db.States.Add(new ItemState { ItemId = prior.Id, Read = true, ReadAt = DateTimeOffset.UtcNow });
        for (var i = 0; i < 3; i++)
        {
            db.Items.Add(new ContentItem { Title = $"A {i}", CanonicalUrl = $"https://example.org/a/{i}", SourceId = sourceA.Id, TopicIds = [topic.Id] });
            db.Items.Add(new ContentItem { Title = $"B {i}", CanonicalUrl = $"https://example.org/b/{i}", SourceId = sourceB.Id, TopicIds = [topic.Id] });
        }
        await db.SaveChangesAsync();
        var ranker = new Ranking(db);
        var first = await ranker.SelectAsync(4, "2026-10-17", null, CancellationToken.None);
        var second = await ranker.SelectAsync(4, "2026-10-17", null, CancellationToken.None);
        Assert.Equal(first.Select(x => x.Item.Id), second.Select(x => x.Item.Id));
        Assert.NotEqual(first[0].Item.SourceId, first[1].Item.SourceId);
        Assert.All(first, x => Assert.Equal(new[] { prior.Id }, x.Evidence));
        Assert.DoesNotContain(first, x => x.Item.Id == prior.Id);
    }

    [Fact]
    public async Task Ranking_reserves_serendipity_and_uses_explicit_saved_feedback()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var lastingA = new Topic { Name = "Lasting A", Weight = 5 };
        var lastingB = new Topic { Name = "Lasting B", Weight = 5 };
        var curiosity = new Topic { Name = "Transient curiosity", Weight = 1, CuriosityOnly = true };
        var enabled = new Source { Name = "Enabled", Url = "https://example.org/feed" };
        var disabled = new Source { Name = "Disabled", Url = "https://example.org/disabled", Enabled = false };
        db.AddRange(lastingA, lastingB, curiosity, enabled, disabled);
        var prior = new ContentItem { Title = "Explicitly saved prior record", CanonicalUrl = "https://example.org/prior-saved", SourceId = enabled.Id, TopicIds = [lastingA.Id] };
        var favored = new ContentItem { Title = "Favored topic", CanonicalUrl = "https://example.org/favored", SourceId = enabled.Id, TopicIds = [lastingA.Id] };
        db.Items.AddRange(prior, favored,
            new ContentItem { Title = "Other lasting 1", CanonicalUrl = "https://example.org/other1", SourceId = enabled.Id, TopicIds = [lastingB.Id] },
            new ContentItem { Title = "Other lasting 2", CanonicalUrl = "https://example.org/other2", SourceId = enabled.Id, TopicIds = [lastingB.Id] },
            new ContentItem { Title = "Unexpected 1", CanonicalUrl = "https://example.org/surprise1", SourceId = enabled.Id, TopicIds = [curiosity.Id] },
            new ContentItem { Title = "Unexpected 2", CanonicalUrl = "https://example.org/surprise2", SourceId = enabled.Id, TopicIds = [curiosity.Id] },
            new ContentItem { Title = "Disabled source item", CanonicalUrl = "https://example.org/disabled-item", SourceId = disabled.Id, TopicIds = [lastingA.Id] });
        db.States.Add(new ItemState { ItemId = prior.Id, Saved = true, SavedAt = DateTimeOffset.UtcNow, Feedback = 1 });
        await SettingsService.SaveAsync(db, new UserSettings { EditionSize = 5, Serendipity = 0.4, Freshness = 0 }, CancellationToken.None);
        var selected = await new Ranking(db).SelectAsync(5, "fixed-seed", new HashSet<Guid> { prior.Id }, CancellationToken.None);
        Assert.Equal(5, selected.Count);
        Assert.Equal(favored.Id, selected[0].Item.Id);
        Assert.Contains(prior.Id, selected[0].Evidence);
        Assert.Equal(2, selected.Count(x => x.Item.TopicIds.Contains(curiosity.Id)));
        Assert.DoesNotContain(selected, x => x.Item.Title == "Disabled source item");
    }

    [Fact]
    public async Task Export_redacts_source_credentials_and_private_storage_names()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Sources.Add(new Source { Name = "Private URL", Url = "https://example.org/feed?api_key=topsecret&q=science" });
            db.Documents.Add(new LibraryDocument { Title = "Private document", FileName = "book.pdf", StorageName = "internal-storage-id.pdf", Sha256 = "test-sha" });
            db.Items.Add(new ContentItem { Title = "Private URL item", CanonicalUrl = "https://example.org/item?access_token=itemsecret", ImageUrl = "https://example.org/image?sig=imagesecret" });
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsync("/api/export", null);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("topsecret", json);
        Assert.DoesNotContain("itemsecret", json);
        Assert.DoesNotContain("imagesecret", json);
        Assert.DoesNotContain("internal-storage-id.pdf", json);
        Assert.Contains("book.pdf", json);
    }

    [Fact]
    public async Task Search_filters_before_pagination_and_validates_filter_values()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Guid topicId, sourceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var topic = new Topic { Name = "Filter topic" };
            var source = new Source { Name = "Filter source", Url = "https://example.org/filter-feed" };
            db.AddRange(topic, source);
            topicId = topic.Id; sourceId = source.Id;
            var date = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
            var first = new ContentItem { Title = "Common filtered paper A", Kind = "paper", CanonicalUrl = "https://example.org/filter-a", SourceId = sourceId, TopicIds = [topicId], PublishedAt = date };
            var second = new ContentItem { Title = "Common filtered paper B", Kind = "paper", CanonicalUrl = "https://example.org/filter-b", SourceId = sourceId, TopicIds = [topicId], PublishedAt = date };
            db.Items.AddRange(first, second,
                new ContentItem { Title = "Common old paper", Kind = "paper", CanonicalUrl = "https://example.org/filter-old", SourceId = sourceId, TopicIds = [topicId], PublishedAt = date.AddYears(-2) },
                new ContentItem { Title = "Common article", Kind = "article", CanonicalUrl = "https://example.org/filter-article", SourceId = sourceId, TopicIds = [topicId], PublishedAt = date });
            db.States.AddRange(new ItemState { ItemId = first.Id, Saved = true }, new ItemState { ItemId = second.Id, Saved = true });
            await db.SaveChangesAsync();
        }
        var url = $"/api/search?q=Common&kind=paper&sourceId={sourceId}&topicId={topicId}&saved=true&read=false&publishedAfter=2026-01-01&publishedBefore=2026-12-31&limit=1&offset=0";
        var filtered = await Read(await client.GetAsync(url));
        Assert.Equal(2, filtered.GetProperty("total").GetInt32());
        Assert.Single(filtered.GetProperty("items").EnumerateArray());
        var browse = await Read(await client.GetAsync("/api/search?saved=true"));
        Assert.Equal(2, browse.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/search?q=Common&publishedAfter=yesterday")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/search?q=Common&topicId=oops")).StatusCode);
    }

    [Fact]
    public async Task Disabling_and_deleting_a_source_preserves_imported_item_state()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Guid sourceId, itemId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = new Source { Name = "Temporary source", Url = "https://example.org/temp-feed" };
            var item = new ContentItem { Title = "Retained source item", CanonicalUrl = "https://example.org/retained-item", SourceId = source.Id };
            sourceId = source.Id; itemId = item.Id;
            db.Sources.Add(source); db.Items.Add(item);
            db.States.Add(new ItemState { ItemId = item.Id, Saved = true, Read = true, ReadAt = DateTimeOffset.UtcNow });
            db.Notes.Add(new Note { ItemId = item.Id, Body = "Retained note" });
            await db.SaveChangesAsync();
        }
        var disabled = await client.PatchAsJsonAsync($"/api/sources/{sourceId}", new { enabled = false });
        disabled.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/sources/{sourceId}")).StatusCode);
        var retained = await Read(await client.GetAsync($"/api/items/{itemId}"));
        Assert.Equal(JsonValueKind.Null, retained.GetProperty("sourceId").ValueKind);
        Assert.True(retained.GetProperty("state").GetProperty("saved").GetBoolean());
        Assert.True(retained.GetProperty("state").GetProperty("read").GetBoolean());
        Assert.Single(retained.GetProperty("notes").EnumerateArray());
    }

    [Fact]
    public async Task Concurrent_daily_requests_share_one_persisted_edition()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Items.Add(new ContentItem { Title = "Supplied fixture", CanonicalUrl = "https://example.org/concurrent-fixture", Provider = "test-fixture" });
            await db.SaveChangesAsync();
        }
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => client.PostAsJsonAsync("/api/discover/today", new { })));
        var editions = await Task.WhenAll(responses.Select(Read));
        Assert.All(editions, e => Assert.Equal(editions[0].GetProperty("id").GetGuid(), e.GetProperty("id").GetGuid()));
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await verifyDb.Editions.CountAsync());
        Assert.Equal(1, await verifyDb.Entries.CountAsync());
    }

    [Fact]
    public async Task Malformed_fields_and_unverified_quotes_return_validation_errors()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var badTopic = await client.PostAsJsonAsync("/api/topics", new { name = 123 });
        Assert.Equal(HttpStatusCode.BadRequest, badTopic.StatusCode);
        var badWeight = await client.PostAsJsonAsync("/api/topics", new { name = "Invalid weight", weight = 1.5 });
        Assert.Equal(HttpStatusCode.BadRequest, badWeight.StatusCode);
        Guid itemId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            itemId = Guid.NewGuid();
            db.Items.Add(new ContentItem { Id = itemId, Title = "Excerpt", CanonicalUrl = "https://example.org/quote-fixture", Body = "This exact source passage was supplied by the feed.", TextProvenance = "feed-content" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/items/{itemId}/state", new { saved = "yes" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/items/{itemId}/notes", new { body = "My note", quote = 123 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/items/{itemId}/notes", new { body = "My note", quote = "A sentence absent from the stored text" })).StatusCode);
        var note = await Read(await client.PostAsJsonAsync($"/api/items/{itemId}/notes", new { body = "My note", quote = "exact source passage" }));
        Assert.Equal("exact source passage", note.GetProperty("quote").GetString());
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }
}
