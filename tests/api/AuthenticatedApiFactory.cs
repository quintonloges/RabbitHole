using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RabbitHole.Data;
using RabbitHole.Features;

namespace RabbitHole.Tests;

/// <summary>Test assembly only: provisions an isolated database and uses the real cookie/CSRF flow.</summary>
public class AuthenticatedApiFactory : WebApplicationFactory<Program>
{
    public const string FixturePassword = "TEST-FIXTURE-password-2026!";
    protected string SecurityFolder { get; } = Path.Combine(Path.GetTempPath(), "rabbithole-auth-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Security:DataProtectionPath", Path.Combine(SecurityFolder, "keys"));
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
    }

    public new HttpClient CreateClient()
    {
        var client = base.CreateClient();
        Task.Run(() => AuthenticateAsync(client)).GetAwaiter().GetResult();
        return client;
    }

    public HttpClient CreateAnonymousClient(Uri? baseAddress = null) => base.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = baseAddress ?? new Uri("http://localhost"), AllowAutoRedirect = false });

    public async Task ProvisionAsync(string password = FixturePassword)
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AdminAuthentication>().SetPasswordAsync(password);
    }

    public async Task AuthenticateAsync(HttpClient client)
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!db.AdminAccounts.Any()) await scope.ServiceProvider.GetRequiredService<AdminAuthentication>().SetPasswordAsync(FixturePassword);
        }
        await RefreshCsrfAsync(client);
        (await client.PostAsJsonAsync("/api/auth/login", new { password = FixturePassword })).EnsureSuccessStatusCode();
        await RefreshCsrfAsync(client);
    }

    public static async Task<string> RefreshCsrfAsync(HttpClient client)
    {
        var result = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = result.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        return token;
    }
}
