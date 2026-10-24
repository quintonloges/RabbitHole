using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class PublicCatalogTests
{
    private sealed class Factory : AuthenticatedApiFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "rabbithole-public-" + Guid.NewGuid().ToString("N") + ".db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("ConnectionStrings:Default", "Data Source=" + _path);
            builder.ConfigureServices(services =>
            {
                foreach (var service in services.Where(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(IngestionWorker)).ToList()) services.Remove(service);
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) foreach (var path in new[] { _path, _path + "-wal", _path + "-shm" })
                try { File.Delete(path); } catch (IOException) { }
        }
    }
    private static ContentItem External(string title, bool isPublic = true) => new()
    {
        Title = "TEST FIXTURE " + title,
        CanonicalUrl = "https://example.org/" + Guid.NewGuid().ToString("N"),
        IsPublic = isPublic,
        Excerpt = "Source excerpt",
        Provider = "rss"
    };
    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, text);
            return JsonDocument.Parse(text).RootElement.Clone();
        }
    }

    [Fact]
    public async Task Anonymous_catalog_search_counts_filters_and_sitemap_exclude_every_private_field_and_record()
    {
        using var factory = new Factory();
        using var anonymous = factory.CreateAnonymousClient();
        Guid publicId, hiddenId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var topic = new Topic { Name = "TEST FIXTURE visible topic", Keywords = "private-topic-keyword-sentinel" };
            var privateTopic = new Topic { Name = "private-only-topic-sentinel" };
            var source = new Source { Name = "TEST FIXTURE visible source", Url = "https://example.org/subscription?token=subscription-secret" };
            var published = External("Visible needle");
            published.TopicIds = [topic.Id]; published.SourceId = source.Id;
            published.Body = "private-body-sentinel"; published.MetadataJson = "{\"secret\":\"private-metadata-sentinel\"}";
            published.Excerpt = new string('x', 700) + "private-truncated-excerpt-sentinel";
            published.ImageUrl = "https://127.0.0.1/private-image";
            published.EmbedUrl = "https://w.soundcloud.com/player/?url=" + Uri.EscapeDataString("http://127.0.0.1/audio");
            publicId = published.Id;
            var hidden = External("private-hidden-sentinel", false); hiddenId = hidden.Id; hidden.TopicIds = [privateTopic.Id];
            var credential = External("private-credential-sentinel"); credential.CanonicalUrl = "https://example.org/item?api_key=secret";
            db.AddRange(topic, privateTopic, source, published, hidden, credential,
                new Note { ItemId = published.Id, Body = "private-note-sentinel" },
                new ItemState { ItemId = published.Id, Saved = true, Read = true, Feedback = 1 });
            await db.SaveChangesAsync();
        }
        var list = await Read(await anonymous.GetAsync("/api/public/items?page=1&pageSize=1"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(publicId, item.GetProperty("id").GetGuid());
        Assert.Equal(600, item.GetProperty("excerpt").GetString()!.Length);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("imageUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("embedUrl").ValueKind);
        var names = item.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(new[] { "id", "title", "canonicalUrl", "author", "publishedAt", "ingestedAt", "type", "provider", "sourceName", "excerpt", "topics", "embedUrl", "imageUrl" }.Order(), names.Order());
        foreach (var query in new[] { "private-body-sentinel", "private-metadata-sentinel", "private-note-sentinel", "private-hidden-sentinel", "private-topic-keyword-sentinel", "private-truncated-excerpt-sentinel" })
            Assert.Equal(0, (await Read(await anonymous.GetAsync("/api/public/items?q=" + query))).GetProperty("total").GetInt32());
        foreach (var route in new[] { "/api/public/filters", "/api/public/sitemap", $"/api/public/items/{publicId}", $"/api/public/items/{publicId}/related" })
        {
            var response = await anonymous.GetAsync(route); response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private-", text); Assert.DoesNotContain("subscription-secret", text);
            Assert.DoesNotContain(hiddenId.ToString(), text);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{hiddenId}")).StatusCode);
        using var check = factory.Services.CreateScope();
        var state = await check.ServiceProvider.GetRequiredService<AppDbContext>().States.SingleAsync();
        Assert.Null(state.LastOpenedAt);
        Assert.True(state.Saved); Assert.True(state.Read);
        Assert.Empty(await check.ServiceProvider.GetRequiredService<AppDbContext>().Editions.ToListAsync());
    }

    [Fact]
    public async Task Document_section_references_close_aliases_and_reject_publication_even_without_document_fields()
    {
        using var factory = new Factory();
        using var admin = factory.CreateClient();
        using var anonymous = factory.CreateAnonymousClient();
        var survivor = External("document survivor");
        var alias = External("document old id"); alias.MergedIntoId = survivor.Id;
        var document = new LibraryDocument { Title = "private-book-title", FileName = "private-file", StorageName = "private-storage", Sha256 = "fixture-document-hash" };
        var section = new DocumentSection { DocumentId = document.Id, ItemId = alias.Id, Title = "private-section", Text = "private extracted text" };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AddRange(document, survivor, alias, section); await db.SaveChangesAsync();
        }
        foreach (var id in new[] { survivor.Id, alias.Id })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{id}/related")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = true })).StatusCode);
        }
        Assert.Equal(0, (await Read(await anonymous.GetAsync("/api/public/items"))).GetProperty("total").GetInt32());
        Assert.Empty((await Read(await anonymous.GetAsync("/api/public/sitemap"))).EnumerateArray());
        var dto = await Read(await admin.GetAsync($"/api/items/{survivor.Id}"));
        Assert.False(dto.GetProperty("canPublish").GetBoolean());
    }

    [Theory]
    [InlineData("local-upload", "metadata")]
    [InlineData("rss", "personal-upload")]
    public async Task Upload_provider_or_provenance_keeps_orphaned_records_and_alias_component_private(string provider, string provenance)
    {
        using var factory = new Factory(); using var admin = factory.CreateClient(); using var anonymous = factory.CreateAnonymousClient();
        var survivor = External("orphaned upload survivor");
        var orphan = External("orphaned upload snapshot");
        orphan.Provider = provider; orphan.TextProvenance = provenance; orphan.MergedIntoId = survivor.Id;
        var alias = External("orphaned upload alias"); alias.MergedIntoId = survivor.Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AddRange(survivor, orphan, alias); await db.SaveChangesAsync();
        }
        Assert.False(PublicCatalogPolicy.IsEligible(orphan));
        foreach (var id in new[] { survivor.Id, orphan.Id, alias.Id })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{id}/related")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/items/{id}/publication", new { isPublic = true })).StatusCode);
        }
        Assert.Equal(0, (await Read(await anonymous.GetAsync("/api/public/items"))).GetProperty("total").GetInt32());
        Assert.Empty((await Read(await anonymous.GetAsync("/api/public/sitemap"))).EnumerateArray());
    }

    [Theory]
    [InlineData("https://example.org/redirect?url=https%3A%2F%2Fexample.com%2Farticle%3Ftoken%3Dprivate-nested")]
    [InlineData("https://example.org/redirect?url=http%3A%2F%2F127.0.0.1%2Fprivate-nested")]
    [InlineData("https://example.org/redirect?url=https%253A%252F%252Fexample.com%252Farticle%253Fsecret_token%253Dprivate-nested")]
    [InlineData("https://example.org/redirect?url=https%3A%2F%2Fexample.com%2Fredirect%3Fnext%3Dhttp%253A%252F%252F10.1.1.1%252Fprivate-nested")]
    [InlineData("https://example.org/redirect?url=https%3A%2F%2Fexample.com%2Fapi%2Flibrary%2Fbooks%2Fprivate-nested%2Ffile")]
    [InlineData("https://example.org/redirect?target=token%253Dprivate-nested")]
    public async Task Nested_private_or_credential_urls_cannot_publish_or_expose_media(string nested)
    {
        using var factory = new Factory(); using var anonymous = factory.CreateAnonymousClient();
        var invalid = External("private nested canonical"); invalid.CanonicalUrl = nested;
        var valid = External("public item with unsafe image"); valid.ImageUrl = nested;
        using (var scope = factory.Services.CreateScope())
        { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.AddRange(invalid, valid); await db.SaveChangesAsync(); }
        Assert.Null(PublicCatalogPolicy.SafeUrl(nested));
        Assert.False(PublicCatalogPolicy.IsEligible(invalid));
        var list = await Read(await anonymous.GetAsync("/api/public/items"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, list.GetProperty("items")[0].GetProperty("imageUrl").ValueKind);
        Assert.DoesNotContain("private-nested", list.GetRawText());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{invalid.Id}")).StatusCode);
    }

    [Fact]
    public async Task Publication_controls_remove_items_immediately_and_get_discovery_never_creates_an_edition()
    {
        using var factory = new Factory();
        using var admin = factory.CreateClient(); using var anonymous = factory.CreateAnonymousClient();
        var item = External("toggle visibility");
        using (var scope = factory.Services.CreateScope())
        { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Items.Add(item); await db.SaveChangesAsync(); }
        var absent = await Read(await admin.GetAsync("/api/discover/today"));
        Assert.Equal(JsonValueKind.Null, absent.GetProperty("id").ValueKind);
        using (var scope = factory.Services.CreateScope()) Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Editions.ToListAsync());
        var edition = await Read(await admin.PostAsJsonAsync("/api/discover/today", new { }));
        Assert.Equal(JsonValueKind.String, edition.GetProperty("id").ValueKind);
        await Read(await admin.PatchAsJsonAsync($"/api/items/{item.Id}/publication", new { isPublic = false }));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{item.Id}")).StatusCode);
        Assert.Empty((await Read(await anonymous.GetAsync("/api/public/sitemap"))).EnumerateArray());
        Assert.Equal(1, (await Read(await admin.GetAsync("/api/items?isPublic=false"))).GetProperty("total").GetInt32());
        await Read(await admin.PatchAsJsonAsync($"/api/items/{item.Id}/publication", new { isPublic = true }));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/public/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Public_filters_page_after_visibility_and_related_ignore_personal_ranking()
    {
        using var factory = new Factory(); using var anonymous = factory.CreateAnonymousClient();
        var first = External("first"); var second = External("second"); var hidden = External("hidden", false);
        var topic = new Topic { Name = "TEST FIXTURE catalog topic" };
        var source = new Source { Name = "TEST FIXTURE catalog source", Enabled = false, Url = "https://example.org/feed" };
        var published = new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        first.TopicIds = [topic.Id]; second.TopicIds = [topic.Id]; hidden.TopicIds = [topic.Id];
        first.SourceId = source.Id; second.SourceId = source.Id;
        first.IngestedAt = published; second.IngestedAt = published.AddDays(1); hidden.IngestedAt = published.AddDays(2);
        first.PublishedAt = published; second.PublishedAt = published.AddDays(1);
        using (var scope = factory.Services.CreateScope())
        { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.AddRange(topic, source, first, second, hidden); await db.SaveChangesAsync(); }
        var page = await Read(await anonymous.GetAsync("/api/public/items?pageSize=1&page=2"));
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(first.Id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var filter = $"topicId={topic.Id}&source={Uri.EscapeDataString(source.Name)}&type=article&from=2026-01-11&to=2026-01-11";
        var filtered = await Read(await anonymous.GetAsync("/api/public/items?" + filter));
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(second.Id, Assert.Single(filtered.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var before = await (await anonymous.GetAsync($"/api/public/items/{first.Id}/related")).Content.ReadAsStringAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var changedTopic = await db.Topics.FindAsync(topic.Id); changedTopic!.Weight = 0; changedTopic.Enabled = false;
            db.States.Add(new ItemState { ItemId = second.Id, Dismissed = true, Read = true, Feedback = -1 });
            db.Notes.Add(new Note { ItemId = first.Id, Body = "private preference" }); await db.SaveChangesAsync();
        }
        var after = await (await anonymous.GetAsync($"/api/public/items/{first.Id}/related")).Content.ReadAsStringAsync();
        Assert.Equal(before, after);
        foreach (var invalid in new[] { "page=0", "pageSize=101", "topicId=bad", "from=bad", "from=2026-02-01&to=2026-01-01" })
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/public/items?" + invalid)).StatusCode);
    }

    [Fact]
    public async Task Sqlite_publication_upgrade_preserves_rows_and_is_idempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var item = External("legacy sqlite", false); db.Items.Add(item); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Items\" DROP COLUMN \"IsPublic\";");
        await SqliteCatalogSchema.ApplyAsync(db); await PublicationUpgrade.ApplyAsync(db);
        db.ChangeTracker.Clear();
        Assert.True((await db.Items.SingleAsync()).IsPublic);
        var durable = await db.Items.SingleAsync(); durable.IsPublic = false; await db.SaveChangesAsync();
        await SqliteCatalogSchema.ApplyAsync(db); await PublicationUpgrade.ApplyAsync(db);
        db.ChangeTracker.Clear(); Assert.False((await db.Items.SingleAsync()).IsPublic);
    }

    private sealed class NoFetch : IRemoteFetcher
    {
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken ct) => throw new InvalidOperationException("No fetch expected");
    }

    [Fact]
    public async Task Backfill_is_once_only_imports_publish_refresh_preserves_hides_and_merges_are_restrictive()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var legacy = External("legacy", false);
        var unsafeRecord = External("credential", false); unsafeRecord.CanonicalUrl = "https://user:password@example.org/private";
        db.AddRange(legacy, unsafeRecord); await db.SaveChangesAsync();
        await PublicationUpgrade.ApplyAsync(db); Assert.True(legacy.IsPublic); Assert.False(unsafeRecord.IsPublic);
        legacy.IsPublic = false; await db.SaveChangesAsync(); await PublicationUpgrade.ApplyAsync(db); Assert.False(legacy.IsPublic);
        var ingestion = new IngestionService(db, new NoFetch(), new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var normalized = new NormalizedItem { Title = "TEST FIXTURE imported", CanonicalUrl = "https://example.org/imported", Provider = "rss" };
        var (imported, created) = await ingestion.UpsertAsync(normalized, null, [], default);
        Assert.True(created); Assert.True(imported.IsPublic);
        imported.IsPublic = false; await db.SaveChangesAsync();
        var (refreshed, _) = await ingestion.UpsertAsync(normalized, null, [], default); Assert.False(refreshed.IsPublic);
        var visible = External("duplicate", true); visible.IngestedAt = imported.IngestedAt.AddMinutes(1);
        db.Items.Add(visible); await db.SaveChangesAsync();
        var survivor = await ItemCatalog.MergeAsync(db, [imported, visible], default); await db.SaveChangesAsync();
        Assert.False(survivor.IsPublic);
        Assert.Null(await PublicCatalog.ResolveAsync(db, visible.Id, await PublicCatalog.EligibleRecordsAsync(db, default), default));
        survivor.IsPublic = true; visible.IsPublic = false; await db.SaveChangesAsync();
        Assert.Null(await PublicCatalog.ResolveAsync(db, visible.Id, await PublicCatalog.EligibleRecordsAsync(db, default), default));
    }

    [Fact]
    public void Safe_public_links_and_approved_embeds_remain_available_with_nested_validation()
    {
        Assert.NotNull(PublicCatalogPolicy.SafeUrl("https://example.org/search?q=token"));
        Assert.NotNull(PublicCatalogPolicy.SafeUrl("https://example.org/redirect?url=https%3A%2F%2Fexample.com%2Farticle"));
        Assert.Equal("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", PublicCatalogPolicy.SafeEmbed("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ?autoplay=1"));
        Assert.Equal("https://w.soundcloud.com/player/?url=https%3A%2F%2Fapi.soundcloud.com%2Ftracks%2F123456", PublicCatalogPolicy.SafeEmbed("https://w.soundcloud.com/player/?url=https%3A%2F%2Fapi.soundcloud.com%2Ftracks%2F123456"));
    }

    [Theory]
    [InlineData("http://localhost/item")]
    [InlineData("https://127.0.0.1/item")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("https://10.1.1.1/item")]
    [InlineData("https://example.internal/item")]
    [InlineData("https://user:password@example.org/item")]
    [InlineData("https://example.org/item?token=private")]
    [InlineData("https://example.org/item#access_token=private")]
    [InlineData("https://example.org/item?secret_token=private")]
    [InlineData("https://example.org/item?access-token=private")]
    [InlineData("https://example.localdomain/item")]
    [InlineData("https://example.org/api/library/books/private-id/file")]
    [InlineData("https://example.org/%61pi%2flibrary/books/private-id/sections")]
    [InlineData("https://example.org/api%252flibrary/books/private-id/file")]
    [InlineData("https://example.org/api/items/private-id")]
    [InlineData("https://example.org/admin/library")]
    public void Credential_and_private_urls_are_ineligible(string url)
    {
        var item = External("unsafe"); item.CanonicalUrl = url;
        Assert.False(PublicCatalogPolicy.IsEligible(item));
    }
}
