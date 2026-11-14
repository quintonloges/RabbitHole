using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using Xunit;

namespace RabbitHole.Tests;

public sealed class DiscoveryBalanceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-14T12:00:00Z");

    [Fact]
    public async Task Public_mix_reaches_quiet_topics_and_limits_publishers_across_page_sizes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var topics = Enumerable.Range(0, 8).Select(n => new Topic { Name = $"TEST FIXTURE Topic {n}" }).ToList();
        db.AddRange(topics);
        foreach (var (topic, index) in topics.Select((t, n) => (t, n)))
        {
            for (var n = 0; n < (index == 0 ? 90 : 9); n++)
                db.Add(new ContentItem
                {
                    Title = $"TEST FIXTURE selection {index} {n}",
                    IsPublic = true,
                    TopicIds = [topic.Id],
                    CanonicalUrl = $"https://publisher-{index}-{n % 3}.example.org/item/{n}",
                    PublishedAt = Now.AddDays(index == 0 ? -1 : -365),
                    IngestedAt = Now.AddMinutes(-n)
                });
        }
        await db.SaveChangesAsync();
        var visible = PublicCatalog.Visible(db);
        var total = await visible.CountAsync();
        var day = DateOnly.FromDateTime(Now.UtcDateTime);
        var first = await PublicDiscovery.PageAsync(visible, total, 0, 24, day, default);
        var idsByTopic = await db.PublicCatalogTopics.ToDictionaryAsync(t => t.ItemId, t => t.TopicId);
        Assert.All(topics, topic => Assert.Equal(3, first.Count(i => idsByTopic[i.Id] == topic.Id)));
        Assert.All(first.GroupBy(i => new Uri(i.CanonicalUrl).Host), group => Assert.InRange(group.Count(), 1, 2));
        var whole = await PublicDiscovery.PageAsync(visible, total, 0, total, day, default);
        var paged = new List<Guid>();
        for (var offset = 0; offset < total; offset += 7)
            paged.AddRange((await PublicDiscovery.PageAsync(visible, total, offset, 7, day, default)).Select(i => i.Id));
        Assert.Equal(whole.Select(i => i.Id), paged);
        Assert.Equal(total, paged.Distinct().Count());
        Assert.Equal(first.Select(i => i.Id), whole.Take(24).Select(i => i.Id));
    }

    [Fact]
    public async Task Private_selection_spreads_publishers_preserves_preferences_and_avoids_read_or_disabled_sources()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var strong = new Topic { Name = "TEST FIXTURE Strong interest", Weight = 5 };
        var quiet = new Topic { Name = "TEST FIXTURE Quiet interest", Weight = 4 };
        var source = new Source { Name = "TEST FIXTURE Disabled", Enabled = false };
        db.AddRange(strong, quiet, source);
        var items = Enumerable.Range(0, 80).Select(n => new ContentItem
        {
            Title = $"TEST FIXTURE pick {n}",
            CanonicalUrl = $"https://publisher-{n % 16}.example.org/{n}",
            TopicIds = [n < 50 ? strong.Id : quiet.Id],
            PublishedAt = Now.AddDays(-1)
        }).ToList();
        items[0].SourceId = source.Id;
        db.AddRange(items);
        db.Add(new ItemState { ItemId = items[1].Id, Read = true, Saved = true });
        await db.SaveChangesAsync();
        await SettingsService.SaveAsync(db, new UserSettings { Serendipity = 0, ArchiveShare = 0, Freshness = 0 }, default);
        var picks = await new Ranking(db).SelectAsync(24, "balance-test", null, default, Now);
        Assert.Equal(24, picks.Count);
        Assert.DoesNotContain(picks, p => p.Item.Id == items[0].Id || p.Item.Id == items[1].Id);
        Assert.All(picks.GroupBy(p => new Uri(p.Item.CanonicalUrl).Host), group => Assert.InRange(group.Count(), 1, 2));
        var quieterCount = picks.Count(p => p.Item.TopicIds.Contains(quiet.Id));
        Assert.InRange(quieterCount, 4, 12);
        Assert.True(picks.Count(p => p.Item.TopicIds.Contains(strong.Id)) > quieterCount);
        Assert.True((await db.States.SingleAsync()).Saved);
        Assert.Equal(5, strong.Weight); Assert.Equal(4, quiet.Weight);
    }

    [Fact]
    public void Multiple_feeds_share_a_publisher_but_independent_video_creators_do_not()
    {
        Assert.Equal(DiscoveryBalance.Publisher("https://www.example.org/ai", "AI feed"),
            DiscoveryBalance.Publisher("https://example.org/music", "Music feed"));
        Assert.NotEqual(DiscoveryBalance.Publisher("https://www.youtube.com/watch?v=first", "Videos", "Artist A"),
            DiscoveryBalance.Publisher("https://www.youtube.com/watch?v=second", "Videos", "Artist B"));
        var balance = new DiscoveryBalance();
        var topic = Guid.NewGuid();
        balance.Add("publisher", [topic]); balance.Add("publisher", [topic]);
        Assert.Equal(new[] { "another" }, balance.SpreadPublishers(new[] { "publisher", "another" }, x => x));
        Assert.Equal(new[] { "publisher" }, balance.SpreadPublishers(new[] { "publisher" }, x => x));
    }
}
