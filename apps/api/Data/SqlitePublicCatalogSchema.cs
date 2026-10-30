using Microsoft.EntityFrameworkCore;

namespace RabbitHole.Data;

public static class SqlitePublicCatalogSchema
{
    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite()) return;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "PublicCatalogEntries" (
                "Id" TEXT NOT NULL PRIMARY KEY REFERENCES "Items" ("Id") ON DELETE CASCADE,
                "IsPublic" INTEGER NOT NULL, "MergedIntoId" TEXT NULL,
                "Title" TEXT NOT NULL, "CanonicalUrl" TEXT NOT NULL, "Author" TEXT NULL,
                "PublishedAt" INTEGER NULL, "PublishedDay" INTEGER NULL,
                "IngestedAt" INTEGER NOT NULL, "UpdatedAt" INTEGER NOT NULL,
                "Type" TEXT NOT NULL, "Provider" TEXT NOT NULL, "SourceName" TEXT NOT NULL,
                "SourceKey" TEXT NOT NULL, "TypeKey" TEXT NOT NULL, "SearchText" TEXT NOT NULL,
                "Excerpt" TEXT NULL, "EmbedUrl" TEXT NULL, "ImageUrl" TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS "PublicCatalogTopics" (
                "ItemId" TEXT NOT NULL REFERENCES "PublicCatalogEntries" ("Id") ON DELETE CASCADE,
                "TopicId" TEXT NOT NULL, "Name" TEXT NOT NULL,
                PRIMARY KEY ("ItemId", "TopicId")
            );
            CREATE INDEX IF NOT EXISTS "IX_PublicCatalogEntries_IsPublic_MergedIntoId_IngestedAt_Id"
                ON "PublicCatalogEntries" ("IsPublic", "MergedIntoId", "IngestedAt", "Id");
            CREATE INDEX IF NOT EXISTS "IX_PublicCatalogEntries_SourceKey" ON "PublicCatalogEntries" ("SourceKey");
            CREATE INDEX IF NOT EXISTS "IX_PublicCatalogEntries_TypeKey" ON "PublicCatalogEntries" ("TypeKey");
            CREATE INDEX IF NOT EXISTS "IX_PublicCatalogEntries_PublishedDay" ON "PublicCatalogEntries" ("PublishedDay");
            CREATE INDEX IF NOT EXISTS "IX_PublicCatalogTopics_TopicId" ON "PublicCatalogTopics" ("TopicId");
            """, ct);
    }
}
