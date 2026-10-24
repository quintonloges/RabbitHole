using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitHole.Data;
using RabbitHole.Features;
using Xunit;

namespace RabbitHole.Tests;

public class AuthenticationTests
{
    private sealed class Factory(string? folder = null, Action<IWebHostBuilder>? configure = null) : AuthenticatedApiFactory
    {
        private readonly string path = folder ?? Path.Combine(Path.GetTempPath(), "rabbithole-security-" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            Directory.CreateDirectory(path);
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("ConnectionStrings:Default", "Data Source=" + Path.Combine(path, "test.db"));
            builder.UseSetting("Security:DataProtectionPath", Path.Combine(path, "keys"));
            configure?.Invoke(builder);
        }
    }

    private static async Task<HttpResponseMessage> Login(HttpClient client, string password = AuthenticatedApiFactory.FixturePassword)
    {
        await AuthenticatedApiFactory.RefreshCsrfAsync(client);
        return await client.PostAsJsonAsync("/api/auth/login", new { password });
    }

    private static string AuthCookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("RabbitHole.Admin=", StringComparison.Ordinal)).Split(';')[0];

    [Fact]
    public async Task Anonymous_legacy_routes_files_and_openapi_require_admin_while_health_is_minimal()
    {
        using var factory = new Factory();
        using var client = factory.CreateAnonymousClient();
        foreach (var route in new[] { "/api/items", "/api/search?q=private", "/api/settings", "/api/topics", "/api/sources", "/api/holes", "/api/export", "/api/library/books", $"/api/library/books/{Guid.NewGuid()}/file", "/openapi/v1.json", "/api/auth/session" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 99);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }
        var health = await client.GetFromJsonAsync<JsonElement>("/api/healthz");
        Assert.Single(health.EnumerateObject());
        Assert.Equal("ok", health.GetProperty("status").GetString());
        // No default credential or first-use public setup exists.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client)).StatusCode);
    }

    [Fact]
    public async Task Login_and_uploads_require_csrf_and_identity_transition_requires_fresh_token()
    {
        using var factory = new Factory(); using var client = factory.CreateAnonymousClient();
        await factory.ProvisionAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { password = AuthenticatedApiFactory.FixturePassword })).StatusCode);
        (await Login(client)).EnsureSuccessStatusCode();
        // Anonymous request token cannot authorize writes after the cookie identity changes.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/topics", new { name = "stale token" })).StatusCode);
        await AuthenticatedApiFactory.RefreshCsrfAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/session")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var upload = new MultipartFormDataContent();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/library/books/upload", upload)).StatusCode);
        await AuthenticatedApiFactory.RefreshCsrfAsync(client);
        (await client.PostAsJsonAsync("/api/topics", new { name = "authenticated fixture topic" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Logout_revokes_replayed_cookie_and_password_recovery_revokes_every_session()
    {
        using var factory = new Factory(); using var first = factory.CreateAnonymousClient(); using var second = factory.CreateAnonymousClient();
        await factory.ProvisionAsync();
        var firstLogin = await Login(first); firstLogin.EnsureSuccessStatusCode();
        var cookie = AuthCookie(firstLogin);
        (await Login(second)).EnsureSuccessStatusCode();
        await AuthenticatedApiFactory.RefreshCsrfAsync(first);
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        using var replay = factory.CreateAnonymousClient(); replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/auth/session")).StatusCode);
        await factory.ProvisionAsync("NEW-TEST-FIXTURE-password-2026!");
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(first)).StatusCode);
        (await Login(first, "NEW-TEST-FIXTURE-password-2026!")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Password_change_checks_current_password_revokes_all_and_preserves_hashed_storage()
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "incorrect", newPassword = "NEW-TEST-FIXTURE-password-2026!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = AuthenticatedApiFactory.FixturePassword, newPassword = "NEW-TEST-FIXTURE-password-2026!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.AdminAccounts.SingleAsync();
        Assert.DoesNotContain("TEST-FIXTURE", account.PasswordHash);
        Assert.All(await db.AdminSessions.ToListAsync(), s => Assert.NotNull(s.RevokedAt));
    }

    [Fact]
    public async Task Expired_database_session_is_rejected_even_with_unexpired_cookie()
    {
        using var factory = new Factory(); using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AdminSessions.SingleAsync()).ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Persisted_keys_and_sessions_survive_application_restart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rabbithole-session-restart-" + Guid.NewGuid().ToString("N"));
        string cookie;
        using (var original = new Factory(folder))
        {
            using var client = original.CreateAnonymousClient(); await original.ProvisionAsync();
            var login = await Login(client); login.EnsureSuccessStatusCode(); cookie = AuthCookie(login);
        }
        using var restarted = new Factory(folder); using var replay = restarted.CreateAnonymousClient();
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/auth/session")).StatusCode);
    }

    [Fact]
    public async Task Repeated_password_failures_lock_the_account_until_lockout_expires()
    {
        using var factory = new Factory(configure: b => { b.UseSetting("Security:LockoutFailures", "2"); b.UseSetting("Security:LoginAttemptsPerWindow", "100"); });
        using var client = factory.CreateAnonymousClient(); await factory.ProvisionAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "incorrect")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "incorrect")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.AdminAccounts.SingleAsync(); Assert.NotNull(account.LockedUntil);
            account.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        }
        (await Login(client)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_public_browsing_and_search_have_configurable_independent_limits()
    {
        using var factory = new Factory(configure: b => { b.UseSetting("Security:LoginAttemptsPerWindow", "2"); b.UseSetting("Security:PublicRequestsPerMinute", "2"); b.UseSetting("Security:SearchRequestsPerMinute", "1"); });
        using var client = factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "incorrect")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "incorrect")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(client, "incorrect")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/public/items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/public/items")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/public/items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/public/items?q=fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/public/items?q=other")).StatusCode);
    }

    [Fact]
    public async Task Untrusted_forwarding_cannot_bypass_explicit_host_validation()
    {
        using var factory = new Factory(); using var client = factory.CreateAnonymousClient();
        using var forged = new HttpRequestMessage(HttpMethod.Get, "/api/healthz");
        forged.Headers.Host = "attacker.invalid";
        forged.Headers.Add("X-Forwarded-Host", "localhost");
        forged.Headers.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(forged)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "attacker.invalid");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/healthz")).StatusCode);
    }

    private sealed class RemoteAddressFilter(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) => { context.Connection.RemoteIpAddress = IPAddress.Parse(address); return continuation(); });
            next(app);
        };
    }

    [Theory]
    [InlineData("192.0.2.10", true)]
    [InlineData("192.0.2.20", false)]
    public async Task Forwarded_scheme_is_honored_only_for_the_exact_configured_proxy(string remote, bool secure)
    {
        using var factory = new Factory(configure: b =>
        {
            b.UseSetting("Security:TrustedProxies", "192.0.2.10");
            b.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(remote)));
        });
        using var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "localhost");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.10");
        var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        var csrfCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("RabbitHole.Csrf="));
        Assert.Equal(secure, csrfCookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SQLite_upgrade_adds_persistent_security_without_rebuilding_existing_data()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE ExistingPersonalData (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL); INSERT INTO ExistingPersonalData VALUES (1, 'preserved');");
        await SqliteSecuritySchema.ApplyAsync(db);
        await SqliteSecuritySchema.ApplyAsync(db);
        db.AdminAccounts.Add(new RabbitHole.Domain.AdminAccount { PasswordHash = "TEST-FIXTURE-hash" });
        db.AdminSessions.Add(new RabbitHole.Domain.AdminSession { ExpiresAt = DateTimeOffset.UtcNow.AddHours(12) });
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.AdminAccounts.CountAsync());
        Assert.Equal(1, await db.AdminSessions.CountAsync());
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT Value FROM ExistingPersonalData WHERE Id=1";
        Assert.Equal("preserved", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Hosted_cookie_is_secure_httponly_host_only_lax_and_expires_in_twelve_hours()
    {
        using var factory = new Factory(configure: b => b.UseEnvironment("Production"));
        using var client = factory.CreateAnonymousClient(new Uri("https://localhost")); await factory.ProvisionAsync();
        var response = await Login(client); response.EnsureSuccessStatusCode();
        var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("RabbitHole.Admin="));
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", header, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.InRange(body.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow, TimeSpan.FromHours(11.99), TimeSpan.FromHours(12.01));
    }
}
