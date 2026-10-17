using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;

namespace RabbitHole.Features;

public sealed class Ranking(AppDbContext db)
{
    public async Task<List<(ContentItem Item, string Reason, List<Guid> Evidence)>> SelectAsync(int count, string seed, HashSet<Guid>? excluded, CancellationToken ct)
    {
        excluded ??= [];
        var topics = await db.Topics.AsNoTracking().Where(t => t.Enabled).ToListAsync(ct);
        var items = await db.Items.AsNoTracking().ToListAsync(ct);
        var states = await db.States.AsNoTracking().ToDictionaryAsync(s => s.ItemId, ct);
        var sources = await db.Sources.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var history = items.Where(i => states.TryGetValue(i.Id, out var s) && (s.Read || s.Saved || s.Feedback != 0)).OrderByDescending(i => states[i.Id].ReadAt ?? states[i.Id].SavedAt ?? states[i.Id].LastOpenedAt ?? DateTimeOffset.MinValue).Take(80).ToList();
        var activeHoleItemIds = (await db.Nodes.AsNoTracking().Where(n => n.ItemId != null).ToListAsync(ct)).OrderByDescending(n => n.CreatedAt).Take(30).Select(n => n.ItemId!.Value).ToList();
        var activeHoleItems = items.Where(i => activeHoleItemIds.Contains(i.Id)).ToList();
        var settings = await SettingsService.ReadAsync(db, ct);
        var lastingTopics = topics.Where(t => !t.CuriosityOnly && t.Weight >= 4).Select(t => t.Id).ToHashSet();
        var explicitTopicSignal = new Dictionary<Guid, double>();
        foreach (var item in items.Where(i => states.ContainsKey(i.Id)))
        {
            var state = states[item.Id];
            var signal = (state.Saved ? 0.15 : 0) + state.Feedback * 0.25;
            foreach (var id in item.TopicIds.Where(id => topics.Any(t => t.Id == id && !t.CuriosityOnly)))
                explicitTopicSignal[id] = explicitTopicSignal.GetValueOrDefault(id) + signal;
        }
        var candidates = new List<Candidate>();
        foreach (var item in items)
        {
            if (excluded.Contains(item.Id) || (states.TryGetValue(item.Id, out var state) && (state.Dismissed || state.Read))) continue;
            if (item.SourceId.HasValue && sources.TryGetValue(item.SourceId.Value, out var source) && !source.Enabled) continue;
            var itemTopics = topics.Where(t => item.TopicIds.Contains(t.Id)).ToList();
            var interest = itemTopics.Count == 0 ? 0.45 : itemTopics.Max(t => t.CuriosityOnly ? Math.Min(0.55, t.Weight / 10.0) : t.Weight / 5.0) + (itemTopics.Any(t => t.Pinned && !t.CuriosityOnly) ? 0.5 : 0);
            var preference = itemTopics.Where(t => !t.CuriosityOnly).Select(t => Math.Clamp(explicitTopicSignal.GetValueOrDefault(t.Id), -0.4, 0.4)).DefaultIfEmpty(0).Max();
            if (states.TryGetValue(item.Id, out var ownState)) preference += (ownState.Saved ? 0.15 : 0) + ownState.Feedback * 0.2;
            var age = (DateTimeOffset.UtcNow - (item.PublishedAt ?? item.IngestedAt)).TotalDays;
            var freshness = Math.Exp(-Math.Max(0, age) / 45.0) * settings.Freshness;
            var evidence = history.Where(h => h.Id != item.Id && h.TopicIds.Intersect(item.TopicIds).Any()).Take(2).Select(h => h.Id).ToList();
            var holeEvidence = activeHoleItems.FirstOrDefault(h => h.Id != item.Id && h.TopicIds.Intersect(item.TopicIds).Any());
            if (holeEvidence != null && !evidence.Contains(holeEvidence.Id)) evidence.Insert(0, holeEvidence.Id);
            var continuity = evidence.Count > 0 ? 0.55 : 0;
            var jitter = (StableHash(seed + item.Id.ToString()) % 1000) / 1000.0 * 0.03;
            var score = interest + preference + freshness + continuity + jitter + (item.Kind == "book-section" ? 0.1 : 0);
            var outside = lastingTopics.Count > 0 && !item.TopicIds.Any(lastingTopics.Contains);
            var reason = evidence.Count > 0 ? "Connected to something you explored" : outside ? "A serendipitous find from your sources" : itemTopics.Count > 0 ? $"From your interest in {itemTopics[0].Name}" : "An unexpected find from your sources";
            candidates.Add(new Candidate(item, score, reason, evidence, outside));
        }
        var chosen = new List<(ContentItem, string, List<Guid>)>();
        var sourceCounts = new Dictionary<string, int>();
        var topicCounts = new Dictionary<Guid, int>();
        var kindCounts = new Dictionary<string, int>();
        var outsideTarget = Math.Min(candidates.Count(c => c.Outside), (int)Math.Ceiling(count * settings.Serendipity));
        var outsideSelected = 0;
        while (chosen.Count < count && candidates.Count > 0)
        {
            var remaining = count - chosen.Count;
            var eligible = outsideSelected < outsideTarget && remaining <= outsideTarget - outsideSelected
                ? candidates.Where(c => c.Outside).ToList()
                : outsideSelected >= outsideTarget && candidates.Any(c => !c.Outside)
                    ? candidates.Where(c => !c.Outside).ToList() : candidates;
            if (eligible.Count == 0) eligible = candidates;
            var next = eligible.OrderByDescending(c => c.Score - 0.35 * sourceCounts.GetValueOrDefault(c.Item.SourceId?.ToString() ?? c.Item.Provider) - c.Item.TopicIds.Sum(id => topicCounts.GetValueOrDefault(id) * 0.2) - 0.08 * kindCounts.GetValueOrDefault(c.Item.Kind))
                .ThenBy(c => StableHash(seed + c.Item.Id.ToString())).First();
            candidates.Remove(next);
            chosen.Add((next.Item, next.Reason, next.Evidence));
            if (next.Outside) outsideSelected++;
            var sourceKey = next.Item.SourceId?.ToString() ?? next.Item.Provider;
            sourceCounts[sourceKey] = sourceCounts.GetValueOrDefault(sourceKey) + 1;
            foreach (var id in next.Item.TopicIds) topicCounts[id] = topicCounts.GetValueOrDefault(id) + 1;
            kindCounts[next.Item.Kind] = kindCounts.GetValueOrDefault(next.Item.Kind) + 1;
        }
        return chosen;
    }

    public async Task<List<(ContentItem Item, string Reason, List<Guid> Evidence)>> RelatedAsync(ContentItem item, int count, CancellationToken ct)
    {
        var all = await db.Items.AsNoTracking().Where(i => i.Id != item.Id).ToListAsync(ct);
        var words = Tokenize(item.Title + " " + item.Excerpt);
        return all.Select(other =>
        {
            var overlap = other.TopicIds.Intersect(item.TopicIds).Count();
            var lexical = Tokenize(other.Title + " " + other.Excerpt).Intersect(words).Count();
            return (Item: other, Score: overlap * 3 + Math.Min(lexical, 5), Overlap: overlap);
        }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Item.Id).Take(count)
        .Select(x => (x.Item, x.Overlap > 0 ? "Shares a topic with this item" : "Related terms in the catalog", new List<Guid> { item.Id })).ToList();
    }

    private static HashSet<string> Tokenize(string? text) => (text ?? "").Split([' ', ',', '.', ':', ';', '/', '-', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 3).Select(x => x.ToLowerInvariant()).ToHashSet();
    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (var ch in value) hash = (hash ^ ch) * 16777619;
        return hash;
    }
    private sealed record Candidate(ContentItem Item, double Score, string Reason, List<Guid> Evidence, bool Outside);
}
