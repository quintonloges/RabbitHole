using Microsoft.EntityFrameworkCore;

namespace RabbitHole.Data;

/// <summary>Upgrades existing SQLite development and fixture databases without rebuilding user data.</summary>
public static class SqliteSecuritySchema
{
    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite()) return;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "AdminAccounts" (
                "Id" INTEGER NOT NULL PRIMARY KEY CHECK ("Id" = 1),
                "PasswordHash" TEXT NOT NULL,
                "FailedAttempts" INTEGER NOT NULL,
                "LockedUntil" TEXT NULL,
                "PasswordChangedAt" TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS "AdminSessions" (
                "Id" TEXT NOT NULL PRIMARY KEY,
                "AccountId" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "ExpiresAt" TEXT NOT NULL,
                "RevokedAt" TEXT NULL,
                FOREIGN KEY ("AccountId") REFERENCES "AdminAccounts" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_AdminSessions_AccountId" ON "AdminSessions" ("AccountId");
            CREATE INDEX IF NOT EXISTS "IX_AdminSessions_ExpiresAt" ON "AdminSessions" ("ExpiresAt");
            """, ct);
    }
}
