using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;

namespace RabbitHole.Features;

/// <summary>Automatic evidence and explicit topic choices are independent persisted facts.</summary>
public static class TopicClassification
{
    public static async Task AssignAsync(AppDbContext db, ContentItem item, IEnumerable<Guid> inherited,
        CancellationToken ct)
    {
        var topics = await db.Topics.ToListAsync(ct);
        var edges = await db.ItemTopics.Where(x => x.ItemId == item.Id).ToListAsync(ct);
        edges = edges.Concat(db.ItemTopics.Local.Where(x => x.ItemId == item.Id)).Distinct().ToList();
        var legacyUserSelection = item.ClassificationOrigin == "user" && edges.All(x => !x.UserOverride.HasValue);
        var memberships = await db.ItemSources.Where(x => x.ItemId == item.Id).ToListAsync(ct);
        memberships = memberships.Concat(db.ItemSources.Local.Where(x => x.ItemId == item.Id)).Distinct().ToList();
        var sources = await db.Sources.ToDictionaryAsync(x => x.Id, ct);
        var sourceTopics = inherited.Concat(memberships.SelectMany(m =>
            m.SourceId is Guid id && sources.TryGetValue(id, out var source) ? source.TopicIds : m.TopicIds)).ToHashSet();
        // Compatibility for records created before the normalized catalog is backfilled.
        if (item.SourceId is Guid primary && sources.TryGetValue(primary, out var primarySource))
            sourceTopics.UnionWith(primarySource.TopicIds);
        var text = string.Join(' ', item.Title, item.Excerpt, item.Body);
        foreach (var topic in topics)
        {
            var edge = edges.FirstOrDefault(x => x.TopicId == topic.Id);
            var keyword = Matches(topic, text);
            if (edge is null)
            {
                edge = new ItemTopic { ItemId = item.Id, TopicId = topic.Id };
                // Backfill legacy user selections (including explicit removals) without guessing.
                if (item.ClassificationOrigin == "user")
                {
                    edge.UserOverride = item.TopicIds.Contains(topic.Id);
                    edge.UserUpdatedAt = item.UpdatedAt;
                }
                else edge.LegacyAssigned = item.TopicIds.Contains(topic.Id) && !sourceTopics.Contains(topic.Id) && !keyword;
                db.ItemTopics.Add(edge);
                edges.Add(edge);
            }
            if (legacyUserSelection)
            {
                edge.UserOverride = item.TopicIds.Contains(topic.Id);
                edge.UserUpdatedAt = item.UpdatedAt;
            }
            edge.SourceAssigned = sourceTopics.Contains(topic.Id);
            edge.KeywordMatched = keyword;
        }
        item.TopicIds = edges.Where(x => topics.Any(t => t.Id == x.TopicId) &&
            (x.UserOverride ?? (x.SourceAssigned || x.KeywordMatched || x.LegacyAssigned)))
            .Select(x => x.TopicId).Distinct().Order().ToList();
        if (item.ClassificationOrigin == "user" || edges.Any(x => x.UserOverride.HasValue)) item.ClassificationOrigin = "user";
        else item.ClassificationOrigin = item.DocumentId.HasValue ? "document-keywords" : "source-keywords";
    }

    public static async Task SetUserTopicsAsync(AppDbContext db, ContentItem item, IReadOnlyCollection<Guid> selected,
        CancellationToken ct)
    {
        await AssignAsync(db, item, [], ct);
        foreach (var edge in db.ItemTopics.Local.Where(x => x.ItemId == item.Id))
        {
            edge.UserOverride = selected.Contains(edge.TopicId);
            edge.UserUpdatedAt = DateTimeOffset.UtcNow;
        }
        item.TopicIds = selected.Distinct().Order().ToList();
        item.ClassificationOrigin = "user";
    }

    public static async Task ReclassifyAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var item in await db.Items.ToListAsync(ct)) await AssignAsync(db, item, [], ct);
        await db.SaveChangesAsync(ct);
    }

    private static bool Matches(Topic topic, string text) => (topic.Keywords + "," + topic.Aliases)
        .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(term => term.Length <= 200)
        .Any(term => Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(term) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)));
}
