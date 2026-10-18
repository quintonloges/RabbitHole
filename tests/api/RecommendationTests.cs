using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using Xunit;

namespace RabbitHole.Tests;

public class RecommendationTests
{
    private sealed class Store : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AppDbContext Db { get; private set; } = null!;
        public async Task Init()
        {
            await connection.OpenAsync();
            Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
            await Db.Database.EnsureCreatedAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private static ContentItem Item(string title, params Guid[] topics) => new() { Title = title, TopicIds = [.. topics], CanonicalUrl = "https://example.org/" + Guid.NewGuid() };

    [Theory]
    [InlineData("A global workspace in language models", "global-workspace")]
    [InlineData("Global Workspace Theory and cognition", "global-workspace")]
    [InlineData("Integrated Information Theory computational limits", "iit")]
    [InlineData("Gradient planning for world models", "world-models")]
    [InlineData("Biquad calculator v3", "biquad")]
    [InlineData("Tactile-guided robot learning", "robot-manipulation")]
    [InlineData("Near-death experiences", "near-death")]
    public void Precise_subjects_are_recognized_without_broad_topic_tags(string title, string expected) =>
        Assert.Contains(Concepts.Match(Item(title)), c => c.Id == expected && c.Field == "title");

    [Theory]
    [InlineData("An office workspace for developers")]
    [InlineData("Integrated information systems for business")]
    [InlineData("Our new corporate model announcement")]
    [InlineData("Framework servicing release version 3")]
    public void Ambiguous_words_do_not_become_specific_concepts(string title) => Assert.Empty(Concepts.Match(Item(title)));

    [Fact]
    public void Younger_Dryas_subject_does_not_imply_support_for_an_impact_hypothesis() =>
        Assert.Equal("Younger Dryas research", Assert.Single(Concepts.Match(Item("Younger Dryas climate transition"))).Label);

    [Fact]
    public void Concepts_use_available_text_and_subject_metadata_but_not_arbitrary_metadata_instructions()
    {
        var item = Item("A sparse title");
        item.Body = "Source text about digital filters and frequency response.";
        item.MetadataJson = """{"keywords":["Global Workspace Theory"],"instructions":"remote viewing"}""";
        var concepts = Concepts.Match(item);
        Assert.Contains(concepts, c => c.Id == "digital-filters" && c.Field == "body");
        Assert.Contains(concepts, c => c.Id == "global-workspace" && c.Field == "metadata");
        Assert.DoesNotContain(concepts, c => c.Id == "remote-viewing");
        item.ConceptTags = ["remote-viewing", "oral tradition"];
        item.ExcludedConcepts = ["global-workspace"];
        Assert.Contains(Concepts.Match(item), c => c.Id == "oral tradition" && c.Field == "user-tag");
        Assert.DoesNotContain(Concepts.Match(item), c => c.Id == "global-workspace");
    }

    [Fact]
    public async Task Curated_and_unknown_trails_and_passive_opens_never_claim_user_exploration()
    {
        await using var store = new Store(); await store.Init(); var db = store.Db;
        var topic = new Topic { Name = "Mind", Weight = 5 };
        var prior = Item("Global Workspace Theory", topic.Id);
        var candidate = Item("Neural broadcasting and global workspace", topic.Id);
        var hole = new RabbitHole.Domain.RabbitHole { Title = "TEST curated", Origin = "curated" };
        var node = new RabbitNode { HoleId = hole.Id, ItemId = prior.Id, Origin = "curated" };
        db.AddRange(topic, prior, candidate, hole, node, new ItemState { ItemId = prior.Id, LastOpenedAt = DateTimeOffset.UtcNow, Understood = true });
        await db.SaveChangesAsync();
        var ranking = new Ranking(db);
        foreach (var origin in new[] { "curated", "example", "legacy-unknown" })
        {
            node.Origin = origin; await db.SaveChangesAsync();
            var selection = Assert.Single(await ranking.SelectAsync(1, "fixed", [prior.Id], default));
            Assert.Empty(selection.Evidence);
            Assert.Equal("interest", selection.Rationale.Kind);
            Assert.DoesNotContain("explored", selection.Reason);
            Assert.DoesNotContain("read", selection.Reason);
            Assert.Equal(0, selection.Rationale.Signals.Single(s => s.Name == "continuity").Value);
        }
        node.Origin = "user"; await db.SaveChangesAsync();
        var userChoice = Assert.Single(await ranking.SelectAsync(1, "fixed", [prior.Id], default));
        Assert.Equal("user-trail", userChoice.Rationale.Kind);
        Assert.Contains("chose for a trail", userChoice.Reason);
        Assert.Contains(prior.Id, userChoice.Evidence);
        Assert.Equal("curated", hole.Origin);
        node.CreatedAt = DateTimeOffset.UtcNow.AddDays(-31); await db.SaveChangesAsync();
        Assert.Empty(Assert.Single(await ranking.SelectAsync(1, "fixed", [prior.Id], default)).Evidence);
    }

    [Theory]
    [InlineData(true, false, 0, "saved")]
    [InlineData(false, true, 0, "read")]
    [InlineData(false, false, 1, "positive-feedback")]
    [InlineData(false, false, -1, "interest")]
    public async Task Rationales_name_only_recorded_actions(bool saved, bool read, int feedback, string kind)
    {
        await using var store = new Store(); await store.Init(); var db = store.Db;
        var topic = new Topic { Name = "Filters", Weight = 5 };
        var prior = Item("Digital filters", topic.Id);
        var next = Item("Digital filters and audio", topic.Id);
        db.AddRange(topic, prior, next, new ItemState { ItemId = prior.Id, Saved = saved, Read = read, Feedback = feedback });
        await db.SaveChangesAsync();
        var result = Assert.Single(await new Ranking(db).SelectAsync(1, "fixed", [prior.Id], default));
        Assert.Equal(kind, result.Rationale.Kind);
        Assert.DoesNotContain("understood", result.Reason);
        Assert.DoesNotContain("explored", result.Reason);
        if (feedback < 0) Assert.Empty(result.Evidence);
        else Assert.Equal(prior.Id, Assert.Single(result.Rationale.Evidence).ItemId);
        Assert.Equal(result.Rationale, RecommendationRationale.FromJson(result.Rationale.ToJson())! with { Evidence = result.Rationale.Evidence, Signals = result.Rationale.Signals });
    }

    [Fact]
    public async Task Robotics_branching_ignores_shared_brand_and_avoids_visited_ancestors()
    {
        await using var store = new Store(); await store.Init(); var db = store.Db;
        var topic = new Topic { Name = "AI" };
        var seed = Item("Brand Nova for Robotics", topic.Id);
        var release = Item("Brand Nova: our next era", topic.Id);
        var plugin = Item("llm-nova 0.35", topic.Id);
        var robot = Item("Tactile-guided robot learning", topic.Id);
        var world = Item("Gradient planning for world models", topic.Id);
        db.AddRange(topic, seed, release, plugin, robot, world); await db.SaveChangesAsync();
        var ranking = new Ranking(db);
        var result = await ranking.RelatedAsync(seed, 20, default);
        Assert.Equal(robot.Id, result[0].Item.Id);
        Assert.Contains(result, r => r.Item.Id == world.Id);
        Assert.DoesNotContain(result, r => r.Item.Id == plugin.Id || r.Item.Id == release.Id);
        Assert.DoesNotContain(await ranking.RelatedAsync(seed, 20, default, new HashSet<Guid> { robot.Id }), r => r.Item.Id == robot.Id);
        Assert.True((await ranking.RelatedAsync(seed, 20, default, new HashSet<Guid> { robot.Id }, true)).Single(r => r.Item.Id == robot.Id).Revisit);
    }

    [Fact]
    public async Task Cross_topic_concepts_and_distinct_directions_survive_source_diversity()
    {
        await using var store = new Store(); await store.Init(); var db = store.Db;
        var ai = new Topic { Name = "AI" }; var mind = new Topic { Name = "Mind" };
        var seed = Item("Global workspace in neural networks", ai.Id);
        var close = Item("Global workspace cognition", mind.Id);
        var duplicate = Item("Global workspace cognition explained", mind.Id);
        var adjacent = Item("Integrated Information Theory", mind.Id);
        var other = Item("AI consciousness and introspective awareness", ai.Id);
        close.CanonicalUrl = "https://one.example/1"; duplicate.CanonicalUrl = "https://one.example/2";
        adjacent.CanonicalUrl = "https://two.example/1"; other.CanonicalUrl = "https://three.example/1";
        db.AddRange(ai, mind, seed, close, duplicate, adjacent, other); await db.SaveChangesAsync();
        var result = await new Ranking(db).RelatedAsync(seed, 4, default);
        Assert.Contains(result.Take(3), r => r.Item.Id == adjacent.Id || r.Item.Id == other.Id);
        Assert.Contains(result, r => r.Item.TopicIds.Contains(mind.Id));
        Assert.All(result, r => Assert.Equal(new[] { seed.Id }, r.Evidence));
        Assert.All(result.Where(r => r.Rationale.Kind == "shared-concept"), r => Assert.Equal(2, r.Rationale.Evidence.Count));
    }

    [Fact]
    public async Task Sparse_catalog_uses_labeled_limited_fallback_and_does_not_pad_empty_matches()
    {
        await using var store = new Store(); await store.Init(); var db = store.Db;
        var topic = new Topic { Name = "Philosophy" };
        var seed = Item("A sparse source", topic.Id);
        db.AddRange(topic, seed, Item("Ethics", topic.Id), Item("Aesthetics", topic.Id), Item("Logic", topic.Id), Item("A completely unrelated record"));
        await db.SaveChangesAsync();
        var result = await new Ranking(db).RelatedAsync(seed, 20, default);
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("broad-topic", r.Rationale.Kind));
        Assert.Empty(await new Ranking(db).RelatedAsync(Item("Unique title without topics"), 20, default));
    }

    [Fact]
    public async Task Routine_updates_are_softly_reduced_but_explicit_preferences_dominate_and_selection_is_stable()
    {
        await using var store = new Store(); await store.Init(); var db = store.Db;
        var interest = new Topic { Name = "Engineering", Weight = 5, Pinned = true };
        var low = new Topic { Name = "Other", Weight = 1 };
        var update = Item("Framework servicing release", interest.Id);
        var essay = Item("How digital filters work", interest.Id);
        var other = Item("How star formation works", low.Id);
        db.AddRange(interest, low, update, essay, other); await db.SaveChangesAsync();
        await SettingsService.SaveAsync(db, new() { Serendipity = 0, Freshness = 0 }, default);
        var ranking = new Ranking(db);
        var first = await ranking.SelectAsync(3, "fixed", null, default);
        Assert.Equal(essay.Id, first[0].Item.Id);
        Assert.True(first.FindIndex(r => r.Item.Id == update.Id) < first.FindIndex(r => r.Item.Id == other.Id));
        Assert.Contains(first, r => r.Item.Id == update.Id);
        Assert.Equal(first.Select(r => r.Item.Id), (await ranking.SelectAsync(3, "fixed", null, default)).Select(r => r.Item.Id));
        db.Add(new ItemState { ItemId = update.Id, Saved = true, Feedback = 1 }); await db.SaveChangesAsync();
        Assert.Equal(update.Id, (await ranking.SelectAsync(1, "fixed", null, default))[0].Item.Id);
    }

    [Fact]
    public async Task Provenance_backfill_preserves_curated_trees_user_state_and_unknown_origins()
    {
        await using var store = new Store(); await store.Init(); var db = store.Db;
        var item = Item("Preserved source");
        var hole = new RabbitHole.Domain.RabbitHole { Id = RecommendationUpgrade.CuratedTrailIds[0], Title = "Edited title retained", Origin = "legacy-unknown" };
        var root = new RabbitNode { HoleId = hole.Id, ItemId = item.Id, Origin = "legacy-unknown", CreatedAt = new(2026, 10, 18, 2, 52, 3, TimeSpan.Zero) };
        var branch = new RabbitNode { HoleId = hole.Id, ParentId = root.Id, ItemId = item.Id, Origin = "user" };
        hole.LastNodeId = branch.Id;
        var unknown = new RabbitHole.Domain.RabbitHole { Title = "From AI workspaces to altered states", Origin = "legacy-unknown" };
        var state = new ItemState { ItemId = item.Id, Saved = true, Read = true, Progress = 0.4 };
        db.AddRange(item, hole, root, branch, unknown, state, new Note { ItemId = item.Id, Body = "Personal note" }); await db.SaveChangesAsync();
        var edition = new FeedEdition { Day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd") };
        var entry = new FeedEntry { EditionId = edition.Id, ItemId = item.Id, Reason = "Connected to something you explored", Position = 0 };
        db.AddRange(edition, entry); await db.SaveChangesAsync();
        await RecommendationUpgrade.ApplyAsync(db); await RecommendationUpgrade.ApplyAsync(db);
        Assert.Equal("curated", hole.Origin); Assert.Equal("curated", root.Origin);
        Assert.Equal("user", branch.Origin); Assert.Equal("legacy-unknown", unknown.Origin);
        Assert.Equal(branch.Id, hole.LastNodeId); Assert.Equal(root.Id, branch.ParentId);
        Assert.Equal("Edited title retained", hole.Title);
        Assert.True((await db.States.SingleAsync()).Read); Assert.Equal(0.4, (await db.States.SingleAsync()).Progress);
        Assert.Equal("Personal note", (await db.Notes.SingleAsync()).Body);
        Assert.Equal(2, await db.Nodes.CountAsync()); Assert.Equal(2, await db.Holes.CountAsync());
        Assert.Contains(":before-recommendation-evidence:", edition.Day);
        Assert.Equal(entry.Id, (await db.Entries.SingleAsync()).Id);
        Assert.Equal("Connected to something you explored", entry.Reason);
    }
}
