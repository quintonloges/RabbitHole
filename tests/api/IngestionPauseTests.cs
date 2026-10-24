using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RabbitHole.Features;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class IngestionPauseTests
{
    private sealed class Factory(bool forced = false) : AuthenticatedApiFactory
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "rabbithole-pause-" + Guid.NewGuid().ToString("N") + ".db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("ConnectionStrings:Default", "Data Source=" + path);
            builder.UseSetting("Ingestion:Paused", forced.ToString());
            builder.ConfigureServices(services =>
            {
                foreach (var service in services.Where(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(IngestionWorker)).ToList()) services.Remove(service);
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
            throw new InvalidOperationException("Paused ingestion must never contact a provider.");
    }

    [Fact]
    public async Task Pausing_blocks_provider_operations_and_preserves_subscriptions_and_local_reads()
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        (await client.PatchAsJsonAsync("/api/settings", new { ingestionPaused = true })).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/sources", new { name = "TEST FIXTURE pause", kind = "rss", url = "https://example.org/feed", enabled = true });
        response.EnsureSuccessStatusCode();
        var source = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = source.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/sources/{id}/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/sources/{id}/test", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/items/import-url", new { url = "https://example.org/article" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/research/search?provider=arxiv&q=curiosity")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/research/import", new { provider = "arxiv", providerId = "1706.03762" })).StatusCode);
        (await client.GetAsync("/api/items")).EnsureSuccessStatusCode();
        var sources = await client.GetFromJsonAsync<JsonElement>("/api/sources");
        Assert.True(sources[0].GetProperty("enabled").GetBoolean());
        Assert.Equal(source.GetProperty("nextSyncAt").GetString(), sources[0].GetProperty("nextSyncAt").GetString());
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/settings")).GetProperty("ingestionPaused").GetBoolean());
        (await client.PatchAsJsonAsync("/api/settings", new { ingestionPaused = false })).EnsureSuccessStatusCode();
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/settings")).GetProperty("ingestionPaused").GetBoolean());
    }

    [Fact]
    public async Task Server_pause_cannot_be_overridden_by_restored_user_preferences()
    {
        using var factory = new Factory(forced: true); using var client = factory.CreateClient();
        (await client.PatchAsJsonAsync("/api/settings", new { ingestionPaused = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/items/import-url", new { url = "https://example.org/article" })).StatusCode);
        (await client.GetAsync("/api/public/items")).EnsureSuccessStatusCode();
    }
}
