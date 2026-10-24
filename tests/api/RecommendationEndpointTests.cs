using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

// These same HTTP tests also run on a disposable PostgreSQL database via the
// repository's recommendation verification container. Never target production.
public class RecommendationEndpointTests
{
    [Fact]
    public async Task Topic_browsing_pages_in_reading_order_and_excludes_dismissed_items_without_changing_the_edition()
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        var ids = await Seed(factory);
        Guid topicId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var topic = new Topic { Name = "TEST FIXTURE browsing" };
            var document = new LibraryDocument { Title = "TEST FIXTURE ordered book", Sha256 = Guid.NewGuid().ToString(), MimeType = "application/pdf" };
            topicId = topic.Id;
            db.Topics.Add(topic); db.Documents.Add(document);
            foreach (var item in await db.Items.Where(i => ids.Contains(i.Id)).ToListAsync())
            {
                var position = Array.IndexOf(ids, item.Id);
                var section = new DocumentSection { DocumentId = document.Id, ItemId = item.Id, Order = position };
                item.TopicIds = [topicId]; item.DocumentId = document.Id; item.SectionId = section.Id;
                item.IngestedAt = DateTimeOffset.UtcNow.AddDays(position);
                db.Sections.Add(section);
            }
            await db.SaveChangesAsync();
        }
        await Read(await client.PatchAsJsonAsync($"/api/items/{ids[0]}/state", new { dismissed = true, saved = true }));
        var edition = await Read(await client.GetAsync("/api/discover/today"));
        var first = await Read(await client.GetAsync($"/api/items?topicId={topicId}&dismissed=false&order=reading&limit=1"));
        var second = await Read(await client.GetAsync($"/api/items?topicId={topicId}&dismissed=false&order=reading&limit=1&offset=1"));
        Assert.Equal(2, first.GetProperty("total").GetInt32());
        Assert.Equal(ids[1], first.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(ids[2], second.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.DoesNotContain(first.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == ids[0]);
        var unfiltered = await Read(await client.GetAsync($"/api/items?topicId={topicId}"));
        Assert.Equal(3, unfiltered.GetProperty("total").GetInt32());
        Assert.Equal(edition.GetRawText(), (await Read(await client.GetAsync("/api/discover/today"))).GetRawText());
        var hidden = await Read(await client.GetAsync($"/api/items/{ids[0]}"));
        Assert.True(hidden.GetProperty("state").GetProperty("saved").GetBoolean());
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"rabbithole-recommendations-{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var postgres = Environment.GetEnvironmentVariable("RABBITHOLE_TEST_POSTGRES");
            if (!string.IsNullOrEmpty(postgres) && new Npgsql.NpgsqlConnectionStringBuilder(postgres).Database?.StartsWith("rabbithole_recommendation_test", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("PostgreSQL checks require a dedicated rabbithole_recommendation_test* database.");
            builder.UseSetting("DATABASE_PROVIDER", postgres == null ? "sqlite" : "postgres");
            builder.UseSetting("ConnectionStrings:Default", postgres ?? "Data Source=" + path);
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
            builder.UseSetting("AI:ApiKey", "");
            builder.UseSetting("AI:BaseUrl", "");
            builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services.Where(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(IngestionWorker)).ToList()) services.Remove(descriptor);
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) foreach (var file in new[] { path, path + "-wal", path + "-shm" }) try { File.Delete(file); } catch (IOException) { }
        }
    }
    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone();
    }
    private static async Task<Guid[]> Seed(Factory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var items = new[] { "Digital filters and biquad filters", "Digital filters and frequency response", "Biquad filters for sound design" }
            .Select(t => new ContentItem { Title = "TEST FIXTURE " + t, Provider = "test-fixture", CanonicalUrl = "https://example.org/" + Guid.NewGuid() }).ToArray();
        db.AddRange(items); await db.SaveChangesAsync(); return items.Select(i => i.Id).ToArray();
    }

    [Fact]
    public async Task Provenance_tags_visited_paths_and_personal_state_persist_through_real_HTTP()
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        var ids = await Seed(factory);
        var hole = await Read(await client.PostAsJsonAsync("/api/holes", new { itemId = ids[0], title = "TEST FIXTURE curated journey", origin = "curated" }));
        var holeId = hole.GetProperty("id").GetGuid(); var rootId = hole.GetProperty("nodes")[0].GetProperty("id").GetGuid();
        Assert.Equal("curated", hole.GetProperty("origin").GetString());
        var child = await Read(await client.PostAsJsonAsync($"/api/holes/{holeId}/nodes", new { itemId = ids[1], parentId = rootId, origin = "user" }));
        Assert.Equal("user", child.GetProperty("origin").GetString());
        var siblings = await Read(await client.PostAsJsonAsync($"/api/holes/{holeId}/nodes", new { itemId = ids[2], parentId = rootId, origin = "example" }));
        await Read(await client.PostAsJsonAsync($"/api/holes/{holeId}/navigate", new { nodeId = rootId }));
        var candidates = await Read(await client.GetAsync($"/api/items/{ids[0]}/related?holeId={holeId}"));
        Assert.DoesNotContain(candidates.EnumerateArray(), c => ids.Contains(c.GetProperty("id").GetGuid()));
        var revisits = await Read(await client.GetAsync($"/api/items/{ids[0]}/related?holeId={holeId}&includeVisited=true"));
        Assert.All(revisits.EnumerateArray().Where(c => ids.Contains(c.GetProperty("id").GetGuid())), c => Assert.True(c.GetProperty("revisit").GetBoolean()));
        Assert.Contains(revisits.EnumerateArray(), c => c.GetProperty("id").GetGuid() == ids[1]);
        var state = await Read(await client.PatchAsJsonAsync($"/api/items/{ids[0]}/state", new { saved = true, read = true, progress = 0.37 }));
        await Read(await client.PostAsJsonAsync($"/api/items/{ids[0]}/notes", new { body = "TEST FIXTURE preserved note" }));
        await Read(await client.PatchAsJsonAsync($"/api/items/{ids[0]}", new { conceptTags = new[] { "oral tradition" }, excludedConcepts = new[] { "biquad" } }));
        var item = await Read(await client.GetAsync($"/api/items/{ids[0]}"));
        Assert.True(item.GetProperty("state").GetProperty("saved").GetBoolean());
        Assert.True(item.GetProperty("state").GetProperty("read").GetBoolean());
        Assert.Equal(0.37, item.GetProperty("state").GetProperty("progress").GetDouble());
        Assert.Equal("TEST FIXTURE preserved note", item.GetProperty("notes")[0].GetProperty("body").GetString());
        Assert.Contains(item.GetProperty("concepts").EnumerateArray(), c => c.GetProperty("id").GetString() == "oral tradition");
        Assert.DoesNotContain(item.GetProperty("concepts").EnumerateArray(), c => c.GetProperty("id").GetString() == "biquad");
        var persisted = await Read(await client.GetAsync($"/api/holes/{holeId}"));
        Assert.Equal(rootId, persisted.GetProperty("lastNodeId").GetGuid());
        Assert.Equal(3, persisted.GetProperty("nodes").GetArrayLength());
        Assert.Equal("curated", persisted.GetProperty("origin").GetString());
        Assert.Equal("curated", persisted.GetProperty("nodes")[0].GetProperty("origin").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/items/{ids[0]}/related?visitedIds=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/holes", new { itemId = ids[0], origin = 123 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/items/{ids[0]}", new { conceptTags = new[] { "biquad" }, excludedConcepts = new[] { "biquad" } })).StatusCode);
    }

    [Fact]
    public async Task Daily_rationales_are_recorded_stable_and_work_without_AI()
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        await Seed(factory);
        var first = await Read(await client.PostAsJsonAsync("/api/discover/refresh-edition", new { }));
        var second = await Read(await client.GetAsync("/api/discover/today"));
        Assert.Equal(first.GetRawText(), second.GetRawText());
        Assert.All(first.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal(2, item.GetProperty("rationale").GetProperty("version").GetInt32());
            Assert.DoesNotContain("Connected to something you explored", item.GetProperty("reason").GetString());
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var editionId = first.GetProperty("id").GetGuid();
        Assert.All(await db.Entries.Where(e => e.EditionId == editionId).ToListAsync(), e => Assert.NotNull(e.RationaleJson));
        var ai = await Read(await client.GetAsync("/api/ai/status"));
        Assert.False(ai.GetProperty("configured").GetBoolean());
        if (Environment.GetEnvironmentVariable("RABBITHOLE_TEST_POSTGRES") != null)
        {
            Assert.True(db.Database.IsNpgsql());
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("RecommendationEvidence"));
        }
    }
}
