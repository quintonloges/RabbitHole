using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using RabbitHole.Domain;

namespace RabbitHole.Features;

public static class ApiSecurity
{
    public static string[] Values(string? value) => (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static void AddApiSecurity(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var allowHttp = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
        var keys = config["Security:DataProtectionPath"] ?? (OperatingSystem.IsWindows() ? Path.Combine(builder.Environment.ContentRootPath, ".local", "auth-keys") : "/data/auth-keys");
        builder.Services.AddDataProtection().SetApplicationName("RabbitHole.Admin").PersistKeysToFileSystem(new DirectoryInfo(keys));
        builder.Services.AddScoped<AdminAuthentication>();
        builder.Services.AddScoped<AdminCookieEvents>();
        builder.Services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);
        builder.Services.AddScoped<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "RabbitHole.Admin";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = allowHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = TimeSpan.FromHours(Math.Clamp(config.GetValue("Security:SessionHours", 12d), .01, 168));
            o.SlidingExpiration = false;
            o.EventsType = typeof(AdminCookieEvents);
        });
        builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = "RabbitHole.Csrf";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = allowHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.Path = "/";
        });
        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.ForwardLimit = 1;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var value in Values(config["Security:TrustedProxies"]))
            {
                if (!IPAddress.TryParse(value, out var ip)) throw new InvalidOperationException("Security:TrustedProxies requires exact IP addresses.");
                o.KnownProxies.Add(ip);
            }
            foreach (var host in Values(config["Security:AllowedHosts"] ?? "localhost,127.0.0.1,::1")) o.AllowedHosts.Add(host);
        });
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)) context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests. Try again later." }, ct);
            };
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path;
                var login = path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase);
                var isPublic = path.StartsWithSegments("/api/public");
                if (!login && !isPublic) return RateLimitPartition.GetNoLimiter("private");
                var search = isPublic && path.Equals("/api/public/items", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(context.Request.Query["q"]);
                var category = login ? "login" : search ? "search" : "public";
                var limit = Math.Clamp(config.GetValue(login ? "Security:LoginAttemptsPerWindow" : search ? "Security:SearchRequestsPerMinute" : "Security:PublicRequestsPerMinute", login ? 5 : search ? 30 : 120), 1, 100_000);
                return RateLimitPartition.GetFixedWindowLimiter(category + ":" + context.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(login ? Math.Clamp(config.GetValue("Security:LoginWindowMinutes", 15), 1, 1440) : 1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            });
        });
    }

    public static void UseApiSecurity(this WebApplication app)
    {
        // Empty KnownProxies normally means trust everyone. Never invoke forwarding in that case.
        if (Values(app.Configuration["Security:TrustedProxies"]).Length > 0) app.UseForwardedHeaders();
        var allowedHosts = Values(app.Configuration["Security:AllowedHosts"] ?? "localhost,127.0.0.1,::1");
        if (allowedHosts.Length == 0 || allowedHosts.Any(x => x.Contains('*'))) throw new InvalidOperationException("Configure explicit Security:AllowedHosts without wildcards.");
        app.Use(async (context, next) =>
        {
            if (!allowedHosts.Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase))
            {
                await Results.Problem(title: "Host not allowed", statusCode: 403).ExecuteAsync(context);
                return;
            }
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            if (!context.Request.Path.StartsWithSegments("/api/public") && !context.Request.Path.Equals("/api/healthz")) context.Response.Headers.CacheControl = "no-store";
            await next();
        });
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint() != null && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS" or "TRACE"))
            {
                var origins = Values(app.Configuration["Security:AllowedOrigins"] ?? app.Configuration["RABBITHOLE_ALLOWED_ORIGINS"] ?? app.Configuration["RABBITHOLE_ALLOWED_ORIGIN"]);
                var origin = context.Request.Headers.Origin.ToString();
                var sameOrigin = origin.Equals($"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase);
                var explicitOrigin = origins.Contains(origin, StringComparer.OrdinalIgnoreCase);
                if ((!string.IsNullOrEmpty(origin) && !sameOrigin && !explicitOrigin) || (context.Request.Headers["Sec-Fetch-Site"] == "cross-site" && !explicitOrigin))
                {
                    await Results.Problem(title: "Cross-site request blocked", statusCode: 403).ExecuteAsync(context);
                    return;
                }
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException)
                {
                    await Results.Problem(title: "CSRF validation failed", detail: "Acquire a fresh token from /api/auth/csrf and send X-CSRF-TOKEN.", statusCode: 400).ExecuteAsync(context);
                    return;
                }
            }
            await next();
        });
    }
}
