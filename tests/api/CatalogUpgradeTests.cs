using System.Linq.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using Xunit;

namespace RabbitHole.Tests;

public class CatalogUpgradeTests
{
    // Reproduce the original SQLite model rather than editing a current-schema database.
    private sealed class LegacyDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Ignore<ItemIdentity>(); builder.Ignore<ItemSource>(); builder.Ignore<ItemTopic>();
            var items = builder.Entity<ContentItem>();
            items.HasQueryFilter((Expression<Func<ContentItem, bool>>?)null);
            foreach (var relationship in items.Metadata.GetForeignKeys().Where(f => f.Properties.Any(p => p.Name == "MergedIntoId")).ToList())
                items.Metadata.RemoveForeignKey(relationship);
            items.Ignore(i => i.MergedIntoId);
            items.HasIndex(i => i.CanonicalUrl).IsUnique();
            items.HasIndex(i => new { i.Provider, i.ProviderId }).IsUnique();
            items.HasIndex(i => i.Doi).IsUnique(); items.HasIndex(i => i.ArxivId).IsUnique();
            builder.Entity<Source>().Ignore(s => s.ResearchCursor).Ignore(s => s.ResearchFromDate)
                .Ignore(s => s.ResearchWindowStartedAt).Ignore(s => s.ResearchCheckpointAt).Ignore(s => s.IdentityVersion);
        }
    }

    [Fact]
    public async Task Existing_sqlite_database_upgrades_twice_without_losing_personal_data_or_topic_choices()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var inherited = new Topic { Name = "TEST FIXTURE source topic", Keywords = "consciousness" };
        var legacyTopic = new Topic { Name = "TEST FIXTURE unknown original assignment" };
        var selected = new Topic { Name = "TEST FIXTURE selected by owner" };
        var source = new Source { Name = "TEST FIXTURE source", Url = "https://example.org/feed", TopicIds = [inherited.Id] };
        var user = new ContentItem
        {
            Title = "TEST FIXTURE consciousness",
            CanonicalUrl = "https://example.org/user",
            SourceId = source.Id,
            Provider = "rss",
            ProviderId = "user-guid",
            TopicIds = [selected.Id],
            ClassificationOrigin = "user"
        };
        var unknown = new ContentItem
        {
            Title = "TEST FIXTURE legacy",
            CanonicalUrl = "https://example.org/legacy",
            SourceId = source.Id,
            Provider = "rss",
            ProviderId = "legacy-guid",
            TopicIds = [legacyTopic.Id],
            ClassificationOrigin = "old-import",
            MetadataJson = """{"providerReferences":[{"provider":"openalex","id":"W4242"},{"provider":"rss","id":"unscoped-old-guid"}]}"""
        };
        var keywordItem = new ContentItem
        {
            Title = "TEST FIXTURE consciousness source text",
            CanonicalUrl = "https://example.org/keyword",
            Provider = "manual",
            ClassificationOrigin = "manual"
        };
        var note = new Note { ItemId = user.Id, Body = "TEST FIXTURE retained note", Quote = "consciousness" };
        var state = new ItemState { ItemId = user.Id, Saved = true, Read = true, Understood = true, Progress = .75, Feedback = 1 };
        var hole = new RabbitHole.Domain.RabbitHole { Title = "TEST FIXTURE trail" };
        var node = new RabbitNode { HoleId = hole.Id, ItemId = user.Id, EvidenceItemIds = [unknown.Id] };
        var prompt = new PracticePrompt { ItemId = user.Id, Question = "TEST FIXTURE question" };
        var attempt = new PracticeAttempt { PromptId = prompt.Id, Response = "TEST FIXTURE answer", Rating = "good" };
        var edition = new FeedEdition { Day = "legacy-fixture" };
        var entry = new FeedEntry { EditionId = edition.Id, ItemId = unknown.Id, Position = 0 };
        await using (var legacy = new LegacyDbContext(options))
        {
            await legacy.Database.EnsureCreatedAsync();
            legacy.AddRange(inherited, legacyTopic, selected, source, user, unknown, keywordItem, note, state, hole, node, prompt, attempt, edition, entry);
            await legacy.SaveChangesAsync();
        }
        await using var db = new AppDbContext(options);
        await SqliteCatalogSchema.ApplyAsync(db);
        await CatalogUpgrade.ApplyAsync(db);
        var initialIdentityKeys = await db.ItemIdentities.OrderBy(x => x.Key).Select(x => x.Key).ToListAsync();
        var originIds = await db.ItemSources.OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
        await SqliteCatalogSchema.ApplyAsync(db);
        await CatalogUpgrade.ApplyAsync(db);
        db.ChangeTracker.Clear();
        Assert.Equal(new[] { user.Id, unknown.Id, keywordItem.Id }.Order(), (await db.Items.ToListAsync()).Select(x => x.Id).Order());
        Assert.Equal(initialIdentityKeys, await db.ItemIdentities.OrderBy(x => x.Key).Select(x => x.Key).ToListAsync());
        Assert.Equal(originIds, await db.ItemSources.OrderBy(x => x.Id).Select(x => x.Id).ToListAsync());
        var userAfter = await db.Items.SingleAsync(x => x.Id == user.Id);
        Assert.Equal(new[] { selected.Id }, userAfter.TopicIds);
        var removal = await db.ItemTopics.SingleAsync(x => x.ItemId == user.Id && x.TopicId == inherited.Id);
        Assert.Equal(false, removal.UserOverride); Assert.True(removal.SourceAssigned); Assert.True(removal.KeywordMatched);
        var legacyAfter = await db.Items.SingleAsync(x => x.Id == unknown.Id);
        Assert.Contains(legacyTopic.Id, legacyAfter.TopicIds);
        Assert.True((await db.ItemTopics.SingleAsync(x => x.ItemId == unknown.Id && x.TopicId == legacyTopic.Id)).LegacyAssigned);
        Assert.Contains(inherited.Id, (await db.Items.SingleAsync(x => x.Id == keywordItem.Id)).TopicIds);
        Assert.Equal(2, await db.ItemSources.CountAsync());
        Assert.All(await db.ItemSources.ToListAsync(), x => { Assert.Equal(source.Id, x.SourceId); Assert.Equal(source.Id, x.SourceKey); Assert.Equal(source.Url, x.Url); });
        Assert.Contains(await db.ItemIdentities.ToListAsync(), x => x.ItemId == unknown.Id && x.Kind == "provider:openalex" && x.Value == "W4242");
        Assert.DoesNotContain(await db.ItemIdentities.ToListAsync(), x => x.Value == "unscoped-old-guid");
        Assert.Equal(note.Id, (await db.Notes.SingleAsync()).Id);
        Assert.Equal("TEST FIXTURE retained note", (await db.Notes.SingleAsync()).Body);
        var stateAfter = await db.States.SingleAsync();
        Assert.Equal(user.Id, stateAfter.ItemId); Assert.True(stateAfter.Saved); Assert.True(stateAfter.Read);
        Assert.True(stateAfter.Understood); Assert.Equal(.75, stateAfter.Progress); Assert.Equal(1, stateAfter.Feedback);
        Assert.Equal(user.Id, (await db.Nodes.SingleAsync(x => x.Id == node.Id)).ItemId);
        Assert.Equal(new[] { unknown.Id }, (await db.Nodes.SingleAsync()).EvidenceItemIds);
        Assert.Equal(user.Id, (await db.Prompts.SingleAsync(x => x.Id == prompt.Id)).ItemId);
        Assert.Equal(prompt.Id, (await db.Attempts.SingleAsync(x => x.Id == attempt.Id)).PromptId);
        Assert.Equal(unknown.Id, (await db.Entries.SingleAsync(x => x.Id == entry.Id)).ItemId);
        Assert.Equal(1, await db.Settings.CountAsync(x => x.Key == CatalogUpgrade.VersionKey));
        var sourceAfter = await db.Sources.SingleAsync();
        Assert.Equal(0, sourceAfter.IdentityVersion);
        Assert.All(await db.ItemSources.ToListAsync(), x => Assert.Equal(0, x.SourceVersion));
        Assert.Null(sourceAfter.ResearchCursor); Assert.Null(sourceAfter.ResearchCheckpointAt);
        sourceAfter.ResearchCursor = "TEST FIXTURE cursor"; sourceAfter.ResearchFromDate = DateTimeOffset.UtcNow;
        sourceAfter.ResearchWindowStartedAt = DateTimeOffset.UtcNow; sourceAfter.ResearchCheckpointAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        await AssertIndexesAndForeignKeysAsync(connection);
        // Old provider GUID uniqueness must no longer prevent distinct source-scoped articles.
        db.Items.Add(new ContentItem { Title = "TEST FIXTURE reused guid", CanonicalUrl = "https://example.net/other", Provider = "rss", ProviderId = "user-guid" });
        await db.SaveChangesAsync();
        Assert.Equal(4, await db.Items.CountAsync());
    }

    [Fact]
    public async Task Current_sqlite_schema_is_safe_to_apply_repeatedly()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var item = new ContentItem { Title = "TEST FIXTURE current", CanonicalUrl = "https://example.org/current" };
        db.Items.Add(item); await db.SaveChangesAsync();
        await SqliteCatalogSchema.ApplyAsync(db); await SqliteCatalogSchema.ApplyAsync(db);
        Assert.Equal(item.Id, (await db.Items.SingleAsync()).Id);
        await AssertIndexesAndForeignKeysAsync(connection);
    }

    private sealed class IntermediateDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Source>().Ignore(s => s.IdentityVersion);
            builder.Entity<ItemSource>().Ignore(s => s.SourceVersion);
            builder.Entity<ItemSource>().HasIndex(s => new { s.ItemId, s.SourceKey }).IsUnique();
        }
    }

    [Fact]
    public async Task Intermediate_catalog_memberships_gain_version_zero_without_losing_existing_origins()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var source = new Source { Name = "TEST FIXTURE intermediate", Url = "https://example.org/feed" };
        var item = new ContentItem { Title = "TEST FIXTURE intermediate article", CanonicalUrl = "https://example.org/article", SourceId = source.Id };
        var origin = new ItemSource { ItemId = item.Id, SourceId = source.Id, SourceKey = source.Id, Name = source.Name, Url = source.Url, Kind = "rss" };
        var note = new Note { ItemId = item.Id, Body = "TEST FIXTURE retained intermediate note" };
        await using (var intermediate = new IntermediateDbContext(options))
        {
            await intermediate.Database.EnsureCreatedAsync();
            intermediate.AddRange(source, item, origin, note); await intermediate.SaveChangesAsync();
        }
        await using var db = new AppDbContext(options);
        await SqliteCatalogSchema.ApplyAsync(db); await SqliteCatalogSchema.ApplyAsync(db);
        var upgraded = await db.ItemSources.SingleAsync();
        Assert.Equal(origin.Id, upgraded.Id); Assert.Equal(0, upgraded.SourceVersion);
        Assert.Equal(source.Id, upgraded.SourceId); Assert.Equal(source.Id, upgraded.SourceKey);
        Assert.Equal(note.Id, (await db.Notes.SingleAsync()).Id);
        var sourceAfter = await db.Sources.SingleAsync(); Assert.Equal(0, sourceAfter.IdentityVersion);
        sourceAfter.IdentityVersion = 1;
        db.ItemSources.Add(new ItemSource { ItemId = item.Id, SourceId = source.Id, SourceKey = source.Id, SourceVersion = 1, Name = "TEST FIXTURE edited source", Kind = "rss", Url = "https://example.net/new-feed" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(new[] { 0, 1 }, await db.ItemSources.OrderBy(x => x.SourceVersion).Select(x => x.SourceVersion).ToListAsync());
        Assert.Equal(1, (await db.Sources.SingleAsync()).IdentityVersion);
        await AssertIndexesAndForeignKeysAsync(connection);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("\"legacy text\"")]
    [InlineData("null")]
    [InlineData("{malformed")]
    [InlineData("{\"providerReferences\":42}")]
    public async Task Backfill_tolerates_malformed_and_nonobject_metadata_without_losing_personal_data(string metadata)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var topic = new Topic { Name = "TEST FIXTURE inherited", Keywords = "consciousness" };
        var source = new Source { Name = "TEST FIXTURE source", Url = "https://example.org/feed", TopicIds = [topic.Id] };
        var item = new ContentItem
        {
            Title = "TEST FIXTURE consciousness",
            CanonicalUrl = "https://example.org/legacy-metadata",
            Provider = "rss",
            ProviderId = "legacy-id",
            SourceId = source.Id,
            ClassificationOrigin = "user",
            TopicIds = [],
            MetadataJson = metadata
        };
        var note = new Note { ItemId = item.Id, Body = "TEST FIXTURE metadata must not erase this note" };
        db.AddRange(topic, source, item, note, new ItemState { ItemId = item.Id, Saved = true, Progress = .4 });
        await db.SaveChangesAsync();
        await CatalogUpgrade.ApplyAsync(db); await CatalogUpgrade.ApplyAsync(db);
        db.ChangeTracker.Clear();
        var after = await db.Items.SingleAsync();
        Assert.Equal(item.Id, after.Id); Assert.Equal(metadata, after.MetadataJson); Assert.Empty(after.TopicIds);
        Assert.Equal(note.Id, (await db.Notes.SingleAsync()).Id);
        var state = await db.States.SingleAsync(); Assert.Equal(item.Id, state.ItemId); Assert.True(state.Saved); Assert.Equal(.4, state.Progress);
        Assert.Equal(source.Id, (await db.ItemSources.SingleAsync()).SourceId);
        Assert.NotEmpty(await db.ItemIdentities.Where(x => x.ItemId == item.Id).ToListAsync());
        Assert.Equal(1, await db.Settings.CountAsync(x => x.Key == CatalogUpgrade.VersionKey));
    }

    private static async Task AssertIndexesAndForeignKeysAsync(SqliteConnection connection)
    {
        await using (var indexes = connection.CreateCommand())
        {
            indexes.CommandText = "PRAGMA index_list(\"Items\")";
            await using var reader = await indexes.ExecuteReaderAsync();
            var found = new HashSet<string>();
            while (await reader.ReadAsync())
                if (reader.GetString(1) is "IX_Items_CanonicalUrl" or "IX_Items_Provider_ProviderId" or "IX_Items_Doi" or "IX_Items_ArxivId")
                { found.Add(reader.GetString(1)); Assert.Equal(0, reader.GetInt64(2)); }
            Assert.Equal(4, found.Count);
        }
        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check";
        await using var foreignKeys = await check.ExecuteReaderAsync();
        Assert.False(await foreignKeys.ReadAsync());
    }
}
