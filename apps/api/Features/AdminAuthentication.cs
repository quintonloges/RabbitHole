using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;

namespace RabbitHole.Features;

public sealed class AdminAuthentication(AppDbContext db, IPasswordHasher<AdminAccount> hasher, IConfiguration config)
{
    // A single administrator account is shared by all requests in this single-instance application.
    private static readonly SemaphoreSlim AccountGate = new(1, 1);
    public static bool ValidPassword(string? password) => password is { Length: >= 12 and <= 256 };

    public async Task<AdminSession?> LoginAsync(string? password, CancellationToken ct)
    {
        await AccountGate.WaitAsync(ct);
        try
        {
            var account = await db.AdminAccounts.SingleOrDefaultAsync(ct);
            if (account == null || account.LockedUntil > DateTimeOffset.UtcNow) return null;
            if (password is not { Length: > 0 and <= 256 } || hasher.VerifyHashedPassword(account, account.PasswordHash, password) == PasswordVerificationResult.Failed)
            {
                account.FailedAttempts++;
                if (account.FailedAttempts >= Math.Clamp(config.GetValue("Security:LockoutFailures", 5), 1, 100))
                {
                    account.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(config.GetValue("Security:LockoutMinutes", 15), 1, 1440));
                    account.FailedAttempts = 0;
                }
                await db.SaveChangesAsync(ct);
                return null;
            }
            account.FailedAttempts = 0;
            account.LockedUntil = null;
            var session = new AdminSession { ExpiresAt = DateTimeOffset.UtcNow.AddHours(Math.Clamp(config.GetValue("Security:SessionHours", 12d), .01, 168)) };
            db.AdminSessions.Add(session);
            // Expired/revoked records carry no personal history and need not grow without bound.
            var sessions = await db.AdminSessions.Where(s => s.AccountId == 1).ToListAsync(ct);
            db.AdminSessions.RemoveRange(sessions.Where(s => s.ExpiresAt < DateTimeOffset.UtcNow.AddDays(-7)));
            await db.SaveChangesAsync(ct);
            return session;
        }
        finally { AccountGate.Release(); }
    }

    public async Task<bool> SetPasswordAsync(string password, string? currentPassword = null, CancellationToken ct = default)
    {
        if (!ValidPassword(password)) throw new ArgumentException("Use a password between 12 and 256 characters.");
        await AccountGate.WaitAsync(ct);
        try
        {
            var account = await db.AdminAccounts.SingleOrDefaultAsync(ct);
            if (currentPassword != null && (account == null || currentPassword.Length > 256 || hasher.VerifyHashedPassword(account, account.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)) return false;
            if (account == null) { account = new AdminAccount(); db.AdminAccounts.Add(account); }
            account.PasswordHash = hasher.HashPassword(account, password);
            account.PasswordChangedAt = DateTimeOffset.UtcNow;
            account.FailedAttempts = 0;
            account.LockedUntil = null;
            foreach (var session in await db.AdminSessions.Where(s => s.RevokedAt == null).ToListAsync(ct)) session.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }
        finally { AccountGate.Release(); }
    }

    public static async Task ProvisionInteractivelyAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected) throw new InvalidOperationException("Administrator provisioning requires an interactive terminal. Passwords must not be passed as arguments or redirected input.");
        Console.Write("New administrator password (12–256 characters): ");
        var password = ReadSecret();
        Console.Write("Confirm password: ");
        var confirmation = ReadSecret();
        if (password != confirmation) throw new InvalidOperationException("Passwords do not match. No changes made.");
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AdminAuthentication>().SetPasswordAsync(password);
        Console.WriteLine("Administrator password saved. All existing sessions were revoked.");
    }

    private static string ReadSecret()
    {
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(key.KeyChar) && value.Length < 257) value.Append(key.KeyChar);
        }
    }
}

public sealed class AdminCookieEvents(AppDbContext db) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var value = context.Principal?.FindFirstValue("session");
        var session = Guid.TryParse(value, out var id) ? await db.AdminSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, context.HttpContext.RequestAborted) : null;
        if (session == null || session.RevokedAt != null || session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) { context.Response.StatusCode = 401; return Task.CompletedTask; }
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) { context.Response.StatusCode = 403; return Task.CompletedTask; }
}

public static class AdminAuthEndpoints
{
    public static void MapAdminAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken })).AllowAnonymous();
        routes.MapGet("/api/auth/session", async (HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var id = Guid.Parse(context.User.FindFirstValue("session")!);
            var session = await db.AdminSessions.AsNoTracking().SingleAsync(s => s.Id == id, ct);
            return Results.Ok(new { authenticated = true, expiresAt = session.ExpiresAt });
        });
        routes.MapPost("/api/auth/login", async (PasswordLogin request, AdminAuthentication auth, HttpContext context, CancellationToken ct) =>
        {
            var session = await auth.LoginAsync(request.Password, ct);
            if (session == null) return Results.Problem(title: "Sign-in failed", detail: "Invalid password, account temporarily locked, or administrator has not been provisioned.", statusCode: 401);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Name, "Administrator"), new Claim("session", session.Id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true, ExpiresUtc = session.ExpiresAt, AllowRefresh = false });
            return Results.Ok(new { authenticated = true, expiresAt = session.ExpiresAt });
        }).AllowAnonymous();
        routes.MapPost("/api/auth/logout", async (HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var id = Guid.Parse(context.User.FindFirstValue("session")!);
            var session = await db.AdminSessions.SingleAsync(s => s.Id == id, ct);
            session.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
        routes.MapPost("/api/auth/password", async (PasswordChange request, AdminAuthentication auth, HttpContext context, CancellationToken ct) =>
        {
            if (!AdminAuthentication.ValidPassword(request.NewPassword) || string.IsNullOrEmpty(request.CurrentPassword)) return Results.BadRequest(new { error = "Provide the current password and a new password between 12 and 256 characters." });
            if (!await auth.SetPasswordAsync(request.NewPassword!, request.CurrentPassword, ct)) return Results.Problem(title: "Current password is incorrect", statusCode: 400);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }
    public sealed record PasswordLogin(string? Password);
    public sealed record PasswordChange(string? CurrentPassword, string? NewPassword);
}
