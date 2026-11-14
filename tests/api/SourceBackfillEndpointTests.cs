using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitHole.Data;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class SourceBackfillEndpointTests
{
    private sealed class Factory : AuthenticatedApiFactory
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "rabbithole-backfill-" + Guid.NewGuid().ToString("N") + ".db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("ConnectionStrings:Default", "Data Source=" + path);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRemoteFetcher>();
                services.AddSingleton<IRemoteFetcher, NoFetch>();
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { }
        }
    }
    private sealed class NoFetch : IRemoteFetcher
    {
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("TEST FIXTURE must not contact a provider.");
    }

    [Fact]
    public async Task Cursor_is_private_and_resets_only_for_archive_identity_changes()
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/sources", new { name = "TEST FIXTURE archive", kind = "rss", url = "https://example.org/feed", enabled = false, backfillUrl = "https://example.org/sitemap.xml", backfillLimit = 2 });
        created.EnsureSuccessStatusCode();
        var row = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(row.TryGetProperty("backfillStateJson", out _));
        Assert.True(row.GetProperty("backfillEnabled").GetBoolean());
        var id = row.GetProperty("id").GetGuid();
        var due = DateTimeOffset.UtcNow.AddDays(1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = await db.Sources.SingleAsync(x => x.Id == id);
            source.BackfillStateJson = "TEST FIXTURE PRIVATE CURSOR";
            source.BackfillNextAt = due;
            source.BackfillStatus = "ready";
            await db.SaveChangesAsync();
        }
        (await client.PatchAsJsonAsync($"/api/sources/{id}", new { name = "TEST FIXTURE renamed", topicIds = Array.Empty<Guid>(), backfillEnabled = false })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var source = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Sources.SingleAsync(x => x.Id == id);
            Assert.Equal("TEST FIXTURE PRIVATE CURSOR", source.BackfillStateJson);
            Assert.Equal(due, source.BackfillNextAt);
        }
        var sources = await client.GetStringAsync("/api/sources");
        Assert.DoesNotContain("PRIVATE CURSOR", sources);
        var export = await client.PostAsync("/api/export", null);
        Assert.DoesNotContain("PRIVATE CURSOR", await export.Content.ReadAsStringAsync());
        (await client.PatchAsJsonAsync($"/api/sources/{id}", new { backfillUrl = "https://example.org/archive.xml" })).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var source = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Sources.SingleAsync(x => x.Id == id);
            Assert.Null(source.BackfillStateJson);
            Assert.Null(source.BackfillNextAt);
            Assert.Equal("pending", source.BackfillStatus);
            Assert.False(source.Enabled);
            Assert.False(source.BackfillEnabled);
        }
    }

    [Fact]
    public async Task Changing_feed_to_research_clears_hidden_archive_url_and_resets_archive_cursor()
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/sources", new
        {
            name = "TEST FIXTURE changing kind",
            kind = "rss",
            url = "https://example.org/feed",
            enabled = false,
            backfillUrl = "https://example.org/sitemap.xml"
        });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = await db.Sources.SingleAsync(x => x.Id == id);
            source.BackfillStateJson = "TEST FIXTURE previous archive cursor";
            source.BackfillNextAt = DateTimeOffset.UtcNow.AddDays(1);
            source.BackfillStatus = "ready";
            await db.SaveChangesAsync();
        }
        var edited = await client.PatchAsJsonAsync($"/api/sources/{id}", new { kind = "arxiv", query = "consciousness", url = "" });
        edited.EnsureSuccessStatusCode();
        using var verify = factory.Services.CreateScope();
        var persisted = await verify.ServiceProvider.GetRequiredService<AppDbContext>().Sources.SingleAsync(x => x.Id == id);
        Assert.Equal("arxiv", persisted.Kind); Assert.Equal("", persisted.BackfillUrl);
        Assert.Null(persisted.BackfillStateJson); Assert.Null(persisted.BackfillNextAt);
        Assert.Equal("pending", persisted.BackfillStatus); Assert.False(persisted.Enabled);
    }

    [Theory]
    [InlineData("http://127.0.0.1/sitemap.xml")]
    [InlineData("https://169.254.169.254/sitemap.xml")]
    [InlineData("https://elsewhere.example/sitemap.xml")]
    [InlineData("https://user:password@example.org/sitemap.xml")]
    public async Task Archive_override_rejects_private_or_unrelated_urls(string archiveUrl)
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        var result = await client.PostAsJsonAsync("/api/sources", new { name = "TEST FIXTURE unsafe", kind = "rss", url = "https://example.org/feed", backfillUrl = archiveUrl });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Theory]
    [InlineData("backfill")]
    [InlineData("backfill-check")]
    public async Task Archive_post_requires_auth_csrf_and_unpaused_enabled_subscription(string action)
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/sources", new { name = "TEST FIXTURE disabled", kind = "youtube", url = "https://www.youtube.com/channel/UC4sdJIrKWKxymZ5lwVSBzTw", enabled = false });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var anonymous = factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/sources/{id}/{action}", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/sources/{id}/{action}", null)).StatusCode);
        await AuthenticatedApiFactory.RefreshCsrfAsync(client);
        var skipped = await client.PostAsync($"/api/sources/{id}/{action}", null);
        skipped.EnsureSuccessStatusCode();
        Assert.Equal("skipped", (await skipped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync($"/api/sources/{id}/{action}")).StatusCode);
        (await client.PatchAsJsonAsync("/api/settings", new { ingestionPaused = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/sources/{id}/{action}", null)).StatusCode);
    }
}
