using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class ArchiveDiscoveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-31T12:00:00Z");
    private static readonly DateOnly Day = new(2026, 10, 31);

    [Theory]
    [InlineData("article", "How oscillators work", true)]
    [InlineData("article", "The history of remote viewing experiments", true)]
    [InlineData("article", "Consciousness and analytic idealism", true)]
    [InlineData("paper", "A model of neural representation", true)]
    [InlineData("article", "Introducing a new AI model", false)]
    [InlineData("article", "How to try our newly launched model", false)]
    [InlineData("article", "New release: a guide to Nova", false)]
    [InlineData("article", "News Briefs 30-10-2026", false)]
    [InlineData("article", "FutureVerb 5.0 update", false)]
    [InlineData("article", "Webinar on consciousness", false)]
    [InlineData("article", "An uninformative title", false)]
    [InlineData("book-section", "How oscillators work", false)]
    public void Archive_cues_distinguish_lasting_formats_from_announcements_without_viewpoint_rules(string kind, string title, bool expected) =>
        Assert.Equal(expected, ArchiveDiscovery.IsCandidate(kind, title, null));

    [Fact]
    public void An_announcement_in_the_excerpt_is_not_promoted_as_an_explainer() =>
        Assert.False(ArchiveDiscovery.IsCandidate("article", "Understanding Nova", "Today we launch our new AI model."));

    [Fact]
    public void Topic_matched_web_archives_do_not_need_an_explainer_keyword_but_exclude_announcements()
    {
        Assert.True(ArchiveDiscovery.IsCandidate("article", "Remote viewing and statistical validation", null, "web-archive"));
        Assert.False(ArchiveDiscovery.IsCandidate("article", "Introducing a new AI model", null, "web-archive"));
    }

    private static ContentItem Item(string title, Guid topic, DateTimeOffset? date) => new()
    {
        Title = "TEST FIXTURE " + title,
        CanonicalUrl = "https://example.org/" + Guid.NewGuid(),
        PublishedAt = date,
        IngestedAt = Now,
        TopicIds = [topic],
        IsPublic = true,
        Kind = "article"
    };

    [Theory]
    [InlineData(30, 16)]
    [InlineData(1, 17)]
    [InlineData(17, 1)]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(0, 0)]
    public async Task Public_mix_has_stable_complete_pagination_and_graceful_sparse_pools(int recentCount, int archiveCount)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var topic = new Topic { Name = "TEST FIXTURE Engineering" };
        var recent = Enumerable.Range(0, recentCount).Select(n => Item($"New model announcement {n}", topic.Id, Now.AddDays(-n))).ToList();
        var archive = Enumerable.Range(0, archiveCount).Select(n => Item($"How circuits work {n}", topic.Id, Now.AddYears(-5))).ToList();
        if (archive.Count > 0) { archive[0].Provider = "web-archive"; archive[0].PublishedAt = null; }
        var hidden = Item("How private circuits work", topic.Id, Now.AddYears(-5)); hidden.IsPublic = false;
        db.Add(topic); db.AddRange(recent); db.AddRange(archive); db.Add(hidden); await db.SaveChangesAsync();
        var query = PublicCatalog.Visible(db);
        var total = recentCount + archiveCount;
        var whole = await PublicDiscovery.PageAsync(query, total, 0, total, Day, default);
        var paged = new List<Guid>();
        for (var offset = 0; offset < total; offset += 7)
            paged.AddRange((await PublicDiscovery.PageAsync(query, total, offset, 7, Day, default)).Select(i => i.Id));
        Assert.Equal(whole.Select(i => i.Id), paged);
        Assert.Equal(total, paged.Distinct().Count()); Assert.DoesNotContain(hidden.Id, paged);
        Assert.Equal(recent.Concat(archive).Select(i => i.Id).Order(), paged.Order());
        Assert.Empty(await PublicDiscovery.PageAsync(query, total, total + 10, 7, Day, default));
        if (recentCount >= 12 && archiveCount >= 4)
        {
            Assert.Equal(4, whole.Take(16).Count(i => i.ArchiveCandidate));
            Assert.All(new[] { 3, 7, 11, 15 }, n => Assert.True(whole[n].ArchiveCandidate));
            var tomorrow = await PublicDiscovery.PageAsync(query, total, 0, 16, Day.AddDays(1), default);
            Assert.NotEqual(whole.Take(16).Where(i => i.ArchiveCandidate).Select(i => i.Id), tomorrow.Where(i => i.ArchiveCandidate).Select(i => i.Id));
        }
    }

    [Fact]
    public async Task Private_archive_slots_require_relevance_and_respect_saved_state_and_disabling_rotation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var topic = new Topic { Name = "TEST FIXTURE Engineering", Weight = 5 };
        var disabledTopic = new Topic { Name = "TEST FIXTURE Disabled", Enabled = false };
        db.AddRange(topic, disabledTopic);
        var fresh = Enumerable.Range(0, 20).Select(n => Item($"Introducing model {n}", topic.Id, Now.AddDays(-2))).ToList();
        var older = Enumerable.Range(0, 20).Select(n => Item($"How circuits work {n}", topic.Id, Now.AddYears(-5))).ToList();
        var offTopic = Item("How stars work", disabledTopic.Id, Now.AddYears(-5));
        var unknown = Item("How unknown circuits work", topic.Id, null);
        var announcement = Item("Introducing an old model", topic.Id, Now.AddYears(-5));
        var source = new Source { Name = "TEST FIXTURE disabled source", Enabled = false };
        older[3].SourceId = source.Id;
        db.AddRange(source, offTopic, unknown, announcement); db.AddRange(fresh); db.AddRange(older);
        db.AddRange(new ItemState { ItemId = older[0].Id, Read = true }, new ItemState { ItemId = older[1].Id, Dismissed = true },
            new ItemState { ItemId = older[2].Id, Feedback = -1 }, new ItemState { ItemId = older[4].Id, Saved = true });
        await db.SaveChangesAsync();
        await SettingsService.SaveAsync(db, new UserSettings { ArchiveShare = 0.25, Serendipity = 0, Freshness = 1 }, default);
        var ranking = new Ranking(db);
        var picks = await ranking.SelectAsync(16, "2026-10-31", [older[5].Id], default, Now);
        var archivePicks = picks.Where(p => p.Rationale.Signals.Any(s => s.Name == "archive-slot" && s.Value == 1)).ToList();
        Assert.Equal(4, archivePicks.Count);
        var excluded = older.Take(4).Select(i => i.Id).Concat(new[] { older[5].Id, offTopic.Id, unknown.Id, announcement.Id }).ToHashSet();
        Assert.All(archivePicks, p => { Assert.DoesNotContain(p.Item.Id, excluded); Assert.Contains("From the archive", p.Rationale.Detail); });
        Assert.Equal(picks.Select(p => p.Item.Id), (await ranking.SelectAsync(16, "2026-10-31", [older[5].Id], default, Now)).Select(p => p.Item.Id));
        Assert.NotEqual(archivePicks.Select(p => p.Item.Id), (await ranking.SelectAsync(16, "2026-11-01", [older[5].Id], default, Now.AddDays(1)))
            .Where(p => p.Rationale.Signals.Any(s => s.Name == "archive-slot" && s.Value == 1)).Select(p => p.Item.Id));
        await SettingsService.SaveAsync(db, new UserSettings { ArchiveShare = 0, Serendipity = 0 }, default);
        Assert.All(await ranking.SelectAsync(16, "2026-10-31", null, default, Now), p => Assert.Equal(0, p.Rationale.Signals.Single(s => s.Name == "archive-slot").Value));
        Assert.True((await db.States.SingleAsync(s => s.ItemId == older[4].Id)).Saved);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("<published>2001-02-03T00:00:00Z</published>", "2001-02-03T00:00:00+00:00")]
    public void Feed_updated_is_never_used_as_original_publication(string published, string? expected)
    {
        var feed = $"<feed xmlns='http://www.w3.org/2005/Atom'><entry><title>TEST FIXTURE</title><id>1</id><link href='https://example.org/essay'/>{published}<updated>2026-10-31T00:00:00Z</updated></entry></feed>";
        var item = Assert.Single(ProviderAdapters.ParseFeed(feed));
        Assert.Equal(expected is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(expected), item.PublishedAt);
    }

    [Fact]
    public void Expanded_presets_offer_real_configured_feeds_and_topic_defaults()
    {
        Assert.True(SourcePresets.All.Count >= 30);
        var distill = Assert.Single(SourcePresets.All, p => p.Name == "Distill");
        Assert.Equal("https://distill.pub/rss.xml", distill.Url);
        Assert.Contains("Frontier AI", distill.Topics);
        Assert.Equal(SourcePresets.All.Count, SourcePresets.All.Select(p => (p.Kind, p.Url, p.Query)).Distinct().Count());
    }
}
