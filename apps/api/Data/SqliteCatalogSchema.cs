using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RabbitHole.Data;

/// <summary>Preserves existing local SQLite databases that predate the normalized catalog.</summary>
public static class SqliteCatalogSchema
{
    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsSqlite()) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var connection = db.Database.GetDbConnection();
        async Task ExecuteAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(ct);
        }
        async Task<HashSet<string>> ColumnsAsync(string table)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = $"PRAGMA table_info(\"{table}\")";
            await using var reader = await command.ExecuteReaderAsync(ct);
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
            return columns;
        }

        var itemColumns = await ColumnsAsync("Items");
        foreach (var column in new[] { "ConceptTags", "ExcludedConcepts" })
            if (!itemColumns.Contains(column)) await ExecuteAsync($"ALTER TABLE \"Items\" ADD COLUMN \"{column}\" TEXT NOT NULL DEFAULT '[]';");
        if (!(await ColumnsAsync("Holes")).Contains("Origin"))
            await ExecuteAsync("ALTER TABLE \"Holes\" ADD COLUMN \"Origin\" TEXT NOT NULL DEFAULT 'legacy-unknown';");
        if (!(await ColumnsAsync("Holes")).Contains("OriginNote"))
            await ExecuteAsync("ALTER TABLE \"Holes\" ADD COLUMN \"OriginNote\" TEXT NULL;");
        if (!(await ColumnsAsync("Nodes")).Contains("Origin"))
            await ExecuteAsync("ALTER TABLE \"Nodes\" ADD COLUMN \"Origin\" TEXT NOT NULL DEFAULT 'legacy-unknown';");
        if (!(await ColumnsAsync("Entries")).Contains("RationaleJson"))
            await ExecuteAsync("ALTER TABLE \"Entries\" ADD COLUMN \"RationaleJson\" TEXT NULL;");
        if (!itemColumns.Contains("MergedIntoId"))
            await ExecuteAsync("ALTER TABLE \"Items\" ADD COLUMN \"MergedIntoId\" TEXT NULL REFERENCES \"Items\" (\"Id\") ON DELETE RESTRICT;");
        var sourceColumns = await ColumnsAsync("Sources");
        foreach (var column in new[] { "ResearchCursor", "ResearchFromDate", "ResearchWindowStartedAt", "ResearchCheckpointAt" })
            if (!sourceColumns.Contains(column)) await ExecuteAsync($"ALTER TABLE \"Sources\" ADD COLUMN \"{column}\" TEXT NULL;");
        if (!sourceColumns.Contains("IdentityVersion"))
            await ExecuteAsync("ALTER TABLE \"Sources\" ADD COLUMN \"IdentityVersion\" INTEGER NOT NULL DEFAULT 0;");

        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS "ItemIdentities" (
                "Key" TEXT NOT NULL CONSTRAINT "PK_ItemIdentities" PRIMARY KEY,
                "ItemId" TEXT NOT NULL,
                "Kind" TEXT NOT NULL,
                "Scope" TEXT NOT NULL,
                "Value" TEXT NOT NULL,
                CONSTRAINT "FK_ItemIdentities_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS "ItemSources" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ItemSources" PRIMARY KEY,
                "ItemId" TEXT NOT NULL,
                "SourceId" TEXT NULL,
                "SourceKey" TEXT NOT NULL,
                "SourceVersion" INTEGER NOT NULL DEFAULT 0,
                "Name" TEXT NOT NULL,
                "Kind" TEXT NOT NULL,
                "Url" TEXT NOT NULL,
                "TopicIds" TEXT NOT NULL,
                "LastSeenAt" TEXT NOT NULL,
                CONSTRAINT "FK_ItemSources_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ItemSources_Sources_SourceId" FOREIGN KEY ("SourceId") REFERENCES "Sources" ("Id") ON DELETE SET NULL
            );
            CREATE TABLE IF NOT EXISTS "ItemTopics" (
                "ItemId" TEXT NOT NULL,
                "TopicId" TEXT NOT NULL,
                "SourceAssigned" INTEGER NOT NULL,
                "KeywordMatched" INTEGER NOT NULL,
                "LegacyAssigned" INTEGER NOT NULL,
                "UserOverride" INTEGER NULL,
                "UserUpdatedAt" TEXT NULL,
                CONSTRAINT "PK_ItemTopics" PRIMARY KEY ("ItemId", "TopicId"),
                CONSTRAINT "FK_ItemTopics_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ItemTopics_Topics_TopicId" FOREIGN KEY ("TopicId") REFERENCES "Topics" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_Items_MergedIntoId" ON "Items" ("MergedIntoId");
            CREATE INDEX IF NOT EXISTS "IX_ItemIdentities_ItemId" ON "ItemIdentities" ("ItemId");
            CREATE INDEX IF NOT EXISTS "IX_ItemSources_SourceId" ON "ItemSources" ("SourceId");
            CREATE INDEX IF NOT EXISTS "IX_ItemTopics_TopicId" ON "ItemTopics" ("TopicId");
            """);
        // The first catalog schema had memberships without source identity versions.
        if (!(await ColumnsAsync("ItemSources")).Contains("SourceVersion"))
            await ExecuteAsync("ALTER TABLE \"ItemSources\" ADD COLUMN \"SourceVersion\" INTEGER NOT NULL DEFAULT 0;");
        await ExecuteAsync("""
            DROP INDEX IF EXISTS "IX_ItemSources_ItemId_SourceKey";
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ItemSources_ItemId_SourceKey_SourceVersion" ON "ItemSources" ("ItemId", "SourceKey", "SourceVersion");
            """);

        var uniqueIndexes = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "PRAGMA index_list(\"Items\")";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) if (reader.GetInt64(2) != 0) uniqueIndexes.Add(reader.GetString(1));
        }
        foreach (var (index, columns) in new[]
        {
            ("IX_Items_CanonicalUrl", "\"CanonicalUrl\""),
            ("IX_Items_Provider_ProviderId", "\"Provider\", \"ProviderId\""),
            ("IX_Items_Doi", "\"Doi\""),
            ("IX_Items_ArxivId", "\"ArxivId\"")
        })
        {
            if (uniqueIndexes.Contains(index)) await ExecuteAsync($"DROP INDEX \"{index}\";");
            await ExecuteAsync($"CREATE INDEX IF NOT EXISTS \"{index}\" ON \"Items\" ({columns});");
        }
        await transaction.CommitAsync(ct);
    }
}
