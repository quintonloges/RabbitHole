using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using System.Text.RegularExpressions;

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
        var memberships = (await db.ItemSources.AsNoTracking().ToListAsync(ct)).ToLookup(x => x.ItemId);
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
            var liveSources = memberships[item.Id].Where(x => x.SourceId.HasValue && sources.ContainsKey(x.SourceId.Value))
                .Select(x => sources[x.SourceId!.Value]).ToList();
            if (liveSources.Count == 0 && item.SourceId is Guid sourceId && sources.TryGetValue(sourceId, out var primary)) liveSources.Add(primary);
            if (liveSources.Count > 0 && liveSources.All(x => !x.Enabled)) continue;
            var itemTopics = topics.Where(t => item.TopicIds.Contains(t.Id)).ToList();
            var interest = itemTopics.Count == 0 ? 0.45 : itemTopics.Max(t => t.CuriosityOnly ? Math.Min(0.55, t.Weight / 10.0) : t.Weight / 5.0) + (itemTopics.Any(t => t.Pinned && !t.CuriosityOnly) ? 0.5 : 0);
            var preference = itemTopics.Where(t => !t.CuriosityOnly).Select(t => Math.Clamp(explicitTopicSignal.GetValueOrDefault(t.Id), -0.4, 0.4)).DefaultIfEmpty(0).Max();
            if (states.TryGetValue(item.Id, out var ownState)) preference += (ownState.Saved ? 0.15 : 0) + ownState.Feedback * 0.2;
            var age = item.PublishedAt is { } published ? (DateTimeOffset.UtcNow - published).TotalDays : double.NaN;
            var freshness = double.IsNaN(age) ? 0 : Math.Exp(-Math.Max(0, age) / 45.0) * settings.Freshness;
            // Rotate a bounded share of older candidates into contention without relabeling their dates.
            var evergreen = age > 180 && StableHash(seed + "evergreen" + item.Id) % 5 == 0 ? 0.45 : 0;
            var evidence = history.Where(h => h.Id != item.Id && h.TopicIds.Intersect(item.TopicIds).Any()).Take(2).Select(h => h.Id).ToList();
            var holeEvidence = activeHoleItems.FirstOrDefault(h => h.Id != item.Id && h.TopicIds.Intersect(item.TopicIds).Any());
            if (holeEvidence != null && !evidence.Contains(holeEvidence.Id)) evidence.Insert(0, holeEvidence.Id);
            var continuity = evidence.Count > 0 ? 0.55 : 0;
            var jitter = (StableHash(seed + item.Id.ToString()) % 1000) / 1000.0 * 0.12;
            var score = interest + preference + freshness + evergreen + continuity + jitter + (item.Kind == "book-section" ? 0.1 : 0);
            var outside = lastingTopics.Count > 0 && !item.TopicIds.Any(lastingTopics.Contains);
            var reason = evidence.Count > 0 ? "Connected to something you explored" : outside ? "A serendipitous find from your sources" : itemTopics.Count > 0 ? $"From your interest in {itemTopics[0].Name}" : "An unexpected find from your sources";
            if (evergreen > 0) reason += "; an older item worth revisiting";
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
            var underCap = eligible.Where(c => sourceCounts.GetValueOrDefault(SourceKey(c.Item)) < Math.Max(2, (int)Math.Ceiling(count * 0.15))).ToList();
            if (underCap.Count > 0) eligible = underCap;
            var next = eligible.OrderByDescending(c => c.Score - 0.35 * sourceCounts.GetValueOrDefault(SourceKey(c.Item)) - c.Item.TopicIds.Sum(id => topicCounts.GetValueOrDefault(id) * 0.2) - 0.08 * kindCounts.GetValueOrDefault(c.Item.Kind)
                - (chosen.Any(x => SimilarTitles(x.Item1, c.Item)) ? 1.5 : 0))
                .ThenBy(c => StableHash(seed + c.Item.Id.ToString())).First();
            candidates.Remove(next);
            chosen.Add((next.Item, next.Reason, next.Evidence));
            if (next.Outside) outsideSelected++;
            var sourceKey = SourceKey(next.Item);
            sourceCounts[sourceKey] = sourceCounts.GetValueOrDefault(sourceKey) + 1;
            foreach (var id in next.Item.TopicIds) topicCounts[id] = topicCounts.GetValueOrDefault(id) + 1;
            kindCounts[next.Item.Kind] = kindCounts.GetValueOrDefault(next.Item.Kind) + 1;
        }
        return chosen;
    }

    public async Task<List<(ContentItem Item, string Reason, List<Guid> Evidence)>> RelatedAsync(ContentItem item, int count, CancellationToken ct)
    {
        var all = await db.Items.AsNoTracking().Where(i => i.Id != item.Id).ToListAsync(ct);
        var dismissed = (await db.States.AsNoTracking().Where(s => s.Dismissed).Select(s => s.ItemId).ToListAsync(ct)).ToHashSet();
        var sources = await db.Sources.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var memberships = (await db.ItemSources.AsNoTracking().ToListAsync(ct)).ToLookup(x => x.ItemId);
        all = all.Where(other =>
        {
            var origins = memberships[other.Id].Where(x => x.SourceId.HasValue && sources.ContainsKey(x.SourceId.Value)).Select(x => sources[x.SourceId!.Value]).ToList();
            if (origins.Count == 0 && other.SourceId is Guid id && sources.TryGetValue(id, out var primary)) origins.Add(primary);
            return !dismissed.Contains(other.Id) && (origins.Count == 0 || origins.Any(s => s.Enabled));
        }).ToList();
        var words = Tokenize(RelatedText(item));
        var title = Tokenize(item.Title);
        var phrases = Phrases(item.Title);
        var tokens = all.ToDictionary(other => other.Id, other => Tokenize(RelatedText(other)));
        var frequencies = tokens.Values.SelectMany(x => x).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        double rarity(string word) => Math.Log(1 + (all.Count + 1.0) / (1 + frequencies.GetValueOrDefault(word)));
        var candidates = all.Select(other =>
        {
            var overlap = other.TopicIds.Intersect(item.TopicIds).Count();
            var shared = tokens[other.Id].Intersect(words).OrderByDescending(rarity).ThenBy(x => x, StringComparer.Ordinal).ToList();
            var otherTitle = Tokenize(other.Title);
            var sharedPhrases = Phrases(RelatedText(other)).Intersect(phrases).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var lexical = shared.Sum(word => rarity(word) * (title.Contains(word) && otherTitle.Contains(word) ? 3 : 1))
                / Math.Sqrt(Math.Max(1, words.Count) * Math.Max(1, tokens[other.Id].Count));
            // One incidental word must not outrank a shared subject phrase. Short feed
            // teasers may use permitted body text already in the catalog for evidence.
            if (shared.Count < 2) lexical *= 0.15;
            var phraseScore = Math.Min(2, sharedPhrases.Count) * 2.0;
            var sameSource = SourceKey(item) == SourceKey(other) ? 0.2 : 0;
            var reason = sharedPhrases.Count > 0 ? "Shared phrase: " + string.Join("; ", sharedPhrases.Take(2))
                : shared.Count >= 2 ? "Shared terms: " + string.Join(", ", shared.Take(3))
                : "Shares a broad topic; an adjacent path to explore";
            return (Item: other, Score: Math.Min(overlap, 2) * 0.35 + lexical * 4 + phraseScore + sameSource, Shared: shared.Count, Overlap: overlap, Reason: reason);
        }).Where(x => x.Overlap > 0 || x.Shared >= 2).ToList();
        var chosen = new List<(ContentItem Item, string Reason, List<Guid> Evidence)>();
        var sourceCounts = new Dictionary<string, int>();
        while (chosen.Count < count && candidates.Count > 0)
        {
            var underCap = candidates.Where(c => sourceCounts.GetValueOrDefault(SourceKey(c.Item)) < 2).ToList();
            var eligible = underCap.Count > 0 ? underCap : candidates;
            var next = eligible.OrderByDescending(c => c.Score - sourceCounts.GetValueOrDefault(SourceKey(c.Item)) * 0.3
                - (chosen.Any(x => SimilarTitles(x.Item, c.Item)) ? 1.5 : 0)).ThenBy(c => c.Item.Id).First();
            candidates.Remove(next);
            chosen.Add((next.Item, next.Reason, [item.Id]));
            var key = SourceKey(next.Item);
            sourceCounts[key] = sourceCounts.GetValueOrDefault(key) + 1;
        }
        return chosen;
    }

    private static readonly HashSet<string> StopWords = "the and for with this that from have what when where which their there about these those into your they been being would could should will than then also more most some such only other each both through across using used uses first new how why all our are can not but its his her who had has does may one two you was were after before under over between without within toward towards article post posted read reading subscribe newsletter copyright rights reserved appears appeared het bericht verscheen eerst foundation abstract paper study research introduction based results show presents work works approach time information encyclopedia blog latest version search project general report sheet".Split(' ').ToHashSet();
    private static HashSet<string> Tokenize(string? text) => Regex.Matches((text ?? "").ToLowerInvariant(), @"\p{L}[\p{L}\p{N}]{2,}")
        .Select(m => m.Value).Where(x => !StopWords.Contains(x)).ToHashSet();
    private static string RelatedText(ContentItem item) => item.Title + " " + item.Excerpt + " " +
        ((item.Excerpt?.Length ?? 0) < 200 && item.Body is { } body ? body[..Math.Min(body.Length, 2500)] : "");
    private static HashSet<string> Phrases(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"\p{L}[\p{L}\p{N}]{2,}").Select(m => m.Value).ToArray();
        return words.Zip(words.Skip(1), (a, b) => (a, b)).Where(pair => !StopWords.Contains(pair.a) && !StopWords.Contains(pair.b))
            .Select(pair => pair.a + " " + pair.b).ToHashSet();
    }
    private static string SourceKey(ContentItem item) => item.SourceId?.ToString() ??
        (Uri.TryCreate(item.CanonicalUrl, UriKind.Absolute, out var uri) ? uri.Host : item.Provider);
    private static bool SimilarTitles(ContentItem a, ContentItem b)
    {
        var left = Tokenize(a.Title); var right = Tokenize(b.Title);
        return left.Count >= 3 && right.Count >= 3 && left.Intersect(right).Count() / (double)left.Union(right).Count() >= 0.8;
    }
    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (var ch in value) hash = (hash ^ ch) * 16777619;
        return hash;
    }
    private sealed record Candidate(ContentItem Item, double Score, string Reason, List<Guid> Evidence, bool Outside);
}
