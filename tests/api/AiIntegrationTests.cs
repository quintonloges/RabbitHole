using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using Xunit;

namespace RabbitHole.Tests;

public class AiIntegrationTests
{
    private sealed class FakeTransport : IAiTransport
    {
        public bool Malformed { get; set; }
        public int Calls { get; private set; }
        public Task<string> CompleteAsync(string action, IReadOnlyList<AiEvidence> evidence, string? response, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Malformed ? "{\"text\":\"unsupported\",\"evidenceItemIds\":[\"not-an-id\"]}" :
                JsonSerializer.Serialize(new { text = "An attributed synthesis of the supplied source.", evidenceItemIds = new[] { evidence[0].Id } }));
        }
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        public readonly FakeTransport Transport = new();
        private readonly string folder = Path.Combine(Path.GetTempPath(), "rabbithole-ai-test-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(folder);
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../apps/api")));
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("ConnectionStrings:Default", "Data Source=" + Path.Combine(folder, "ai.db"));
            builder.UseSetting("Documents:Path", Path.Combine(folder, "uploads"));
            builder.UseSetting("AI:BaseUrl", "http://localhost:1234/v1");
            builder.UseSetting("AI:Model", "fixture-model");
            builder.UseSetting("AI:AllowLocal", "true");
            builder.UseSetting("AI:ApiKey", "TEST-SECRET-NEVER-EXPORTED");
            builder.ConfigureServices(s => { s.RemoveAll<IAiTransport>(); s.AddSingleton<IAiTransport>(Transport); });
        }
    }

    [Fact]
    public async Task Ai_is_opt_in_grounded_cached_and_fails_without_breaking_original()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new ContentItem { Title = "AI TEST FIXTURE", CanonicalUrl = "https://example.org/ai-fixture", Body = new string('a', 200), TextProvenance = "feed-content" };
            id = item.Id; db.Items.Add(item); await db.SaveChangesAsync();
        }
        var disabled = await client.PostAsJsonAsync($"/api/items/{id}/ai", new { action = "summary" });
        Assert.Equal(HttpStatusCode.Conflict, disabled.StatusCode);
        Assert.Equal(0, factory.Transport.Calls);
        (await client.PatchAsJsonAsync("/api/settings", new { aiEnabled = true })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/items/{id}/ai", new { action = "summary" })).EnsureSuccessStatusCode();
        var cached = await client.PostAsJsonAsync($"/api/items/{id}/ai", new { action = "summary" });
        cached.EnsureSuccessStatusCode();
        Assert.Equal(1, factory.Transport.Calls);
        var parsed = await cached.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, parsed.GetProperty("evidenceItemIds")[0].GetGuid());
        factory.Transport.Malformed = true;
        var invalid = await client.PostAsJsonAsync($"/api/items/{id}/ai", new { action = "challenge" });
        Assert.Equal(HttpStatusCode.BadGateway, invalid.StatusCode);
        (await client.GetAsync($"/api/items/{id}")).EnsureSuccessStatusCode();
        var export = await client.PostAsJsonAsync("/api/export", new { });
        Assert.DoesNotContain("TEST-SECRET", await export.Content.ReadAsStringAsync());
        Assert.DoesNotContain("TEST-SECRET", await client.GetStringAsync("/api/ai/status"));
    }
}
