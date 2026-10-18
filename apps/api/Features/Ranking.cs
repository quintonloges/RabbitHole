using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using System.Text.RegularExpressions;

namespace RabbitHole.Features;

public sealed class Ranking(AppDbContext db)
{
    public async Task<List<Recommendation>> SelectAsync(int count, string seed, HashSet<Guid>? excluded, CancellationToken ct, DateTimeOffset? asOf = null)
    {
        excluded ??= [];
        var now = asOf ?? DateTimeOffset.UtcNow;
        var topics = await db.Topics.AsNoTracking().Where(t => t.Enabled).ToListAsync(ct);
        var items = await db.Items.AsNoTracking().ToListAsync(ct);
        var states = await db.States.AsNoTracking().ToDictionaryAsync(s => s.ItemId, ct);
        var eligibleIds = await EligibleIds(items, states, ct);
        var concepts = items.ToDictionary(i => i.Id, i => Concepts.Focus(Concepts.Match(i)));
        // A user-created turn is an explicit choice. Loading/navigating an example
        // does not change its provenance or establish reading/understanding.
        var turns = (await db.Nodes.AsNoTracking().Where(n => n.Origin == "user" && n.ItemId != null).ToListAsync(ct))
            .Where(n => n.CreatedAt >= now.AddDays(-30)).OrderByDescending(n => n.CreatedAt).Take(30).Select(n => n.ItemId!.Value).ToHashSet();
        var history = items.Where(i => states.TryGetValue(i.Id, out var s) && (s.Read || s.Saved || s.Feedback > 0) || turns.Contains(i.Id))
            .Where(i => !states.TryGetValue(i.Id, out var s) || !s.Dismissed && s.Feedback >= 0)
            .OrderByDescending(i => states.GetValueOrDefault(i.Id)?.ReadAt ?? states.GetValueOrDefault(i.Id)?.SavedAt ?? DateTimeOffset.MinValue).ThenBy(i => i.Id).Take(80).ToList();
        var settings = await SettingsService.ReadAsync(db, ct);
        var lastingTopics = topics.Where(t => !t.CuriosityOnly && t.Weight >= 4).Select(t => t.Id).ToHashSet();
        var topicSignal = new Dictionary<Guid, double>();
        foreach (var item in items.Where(i => states.ContainsKey(i.Id)))
        {
            var state = states[item.Id];
            var signal = (state.Saved ? 0.15 : 0) + state.Feedback * 0.25;
            foreach (var id in item.TopicIds.Where(id => topics.Any(t => t.Id == id && !t.CuriosityOnly)))
                topicSignal[id] = topicSignal.GetValueOrDefault(id) + signal;
        }
        var candidates = new List<DailyCandidate>();
        foreach (var item in items.Where(i => eligibleIds.Contains(i.Id) && !excluded.Contains(i.Id) && states.GetValueOrDefault(i.Id)?.Read != true))
        {
            var matchedTopics = topics.Where(t => item.TopicIds.Contains(t.Id)).OrderByDescending(Interest).ThenBy(t => t.Id).ToList();
            var topic = matchedTopics.FirstOrDefault();
            var interest = topic is null ? 0.45 : Interest(topic);
            var preference = matchedTopics.Where(t => !t.CuriosityOnly).Select(t => Math.Clamp(topicSignal.GetValueOrDefault(t.Id), -0.4, 0.4)).DefaultIfEmpty(0).Max();
            var ownState = states.GetValueOrDefault(item.Id);
            preference += (ownState?.Saved == true ? 0.3 : 0) + (ownState?.Feedback ?? 0) * 0.35;
            var age = item.PublishedAt is { } published ? (now - published).TotalDays : double.NaN;
            var freshness = double.IsNaN(age) ? 0 : Math.Exp(-Math.Max(0, age) / 45.0) * settings.Freshness;
            var evergreen = age > 180 && StableHash(seed + "evergreen" + item.Id) % 5 == 0 ? 0.3 : 0;
            var connections = history.Where(h => h.Id != item.Id).Select(h => new
            {
                Item = h,
                Concept = concepts[item.Id].FirstOrDefault(c => concepts[h.Id].Any(m => m.Id == c.Id)),
                Topic = matchedTopics.FirstOrDefault(t => h.TopicIds.Contains(t.Id))
            }).Where(h => h.Concept != null || h.Topic != null).OrderByDescending(h => h.Concept != null).ThenBy(h => h.Item.Id).Take(1).ToList();
            var evidence = new List<RecommendationEvidence>();
            var continuity = 0.0;
            var summary = topic is null ? "An unexpected discovery from your collection" : topic.CuriosityOnly
                ? $"For your temporary curiosity in {topic.Name}" : $"From your interest in {topic.Name}";
            var kind = topic is null ? "serendipity" : topic.CuriosityOnly ? "curiosity" : "interest";
            if (topic is { Weight: 0, Pinned: false }) { summary = $"A discovery in {topic.Name}"; kind = "catalog"; }
            if (concepts[item.Id].FirstOrDefault() is { } subject) summary += $" · {subject.Label}";
            foreach (var connection in connections)
            {
                var state = states.GetValueOrDefault(connection.Item.Id);
                var relationship = state?.Saved == true ? "saved" : state?.Read == true ? "read" : state?.Feedback > 0 ? "positive-feedback" : "user-trail";
                continuity = connection.Concept is null ? 0.08 : relationship == "user-trail" ? 0.2 : 0.35;
                evidence.Add(new(connection.Item.Id, connection.Item.Title, relationship, connection.Concept?.Label ?? connection.Topic?.Name));
                if (connection.Concept is { } concept)
                {
                    var verb = relationship switch { "saved" => "saved", "read" => "marked read", "positive-feedback" => "liked", _ => "chose for a trail" };
                    summary = $"More on {concept.Label}, from a source you {verb}";
                    kind = relationship;
                }
            }
            var format = FormatSignal(item);
            var formatWeight = ownState?.Saved == true || ownState?.Feedback > 0 ? Math.Max(0, format.Value) : format.Value;
            var jitter = StableHash(seed + item.Id) % 1000 / 1000.0 * 0.12;
            var score = interest + preference + freshness + evergreen + continuity + formatWeight + jitter;
            var outside = lastingTopics.Count > 0 && !item.TopicIds.Any(lastingTopics.Contains);
            var rationale = new RecommendationRationale
            {
                Kind = kind,
                Summary = summary,
                Evidence = evidence,
                Detail = string.Join(" ", new[] {
                    age > 180 ? "An older source from the collection." : double.IsNaN(age) ? "The source's publication date is unknown." : null,
                    format.Detail,
                    connections.Any(c => c.Concept == null) ? $"A small continuity signal comes from the broad topic {connections[0].Topic?.Name}; no closer concept match was found." : null
                }.Where(x => x != null)),
                Signals = [new("interest", interest, topic is null ? "No matching enabled topic" : $"{topic.Name}: weight {topic.Weight}; {(topic.CuriosityOnly ? "temporary curiosity" : "lasting interest")}"),
                    new("explicit-preference", preference, "Bounded saved-item and More/Less like this feedback"),
                    new("continuity", continuity, "Only saved, explicitly read, liked, or user-created trail items; opens alone add no continuity"),
                    new("freshness", freshness, item.PublishedAt?.ToString("yyyy-MM-dd") ?? "Unknown publication date"),
                    new("older-rotation", evergreen, "Deterministic rotation of sources older than 180 days"),
                    new("format", formatWeight, format.Detail), new("tie-break", jitter, "Stable daily seed")]
            };
            candidates.Add(new(item, rationale, score, outside));
        }
        var chosen = new List<Recommendation>();
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
                : outsideSelected >= outsideTarget && candidates.Any(c => !c.Outside) ? candidates.Where(c => !c.Outside).ToList() : candidates;
            if (eligible.Count == 0) eligible = candidates;
            var underCap = eligible.Where(c => sourceCounts.GetValueOrDefault(SourceKey(c.Item)) < Math.Max(2, (int)Math.Ceiling(count * 0.15))).ToList();
            if (underCap.Count > 0) eligible = underCap;
            double Diversity(DailyCandidate c) => -0.35 * sourceCounts.GetValueOrDefault(SourceKey(c.Item)) - c.Item.TopicIds.Sum(id => topicCounts.GetValueOrDefault(id) * 0.2)
                - 0.08 * kindCounts.GetValueOrDefault(c.Item.Kind) - (chosen.Any(x => SimilarTitles(x.Item, c.Item)) ? 1.5 : 0);
            var next = eligible.OrderByDescending(c => c.Score + Diversity(c)).ThenBy(c => StableHash(seed + c.Item.Id)).ThenBy(c => c.Item.Id).First();
            candidates.Remove(next);
            chosen.Add(new(next.Item, next.Rationale with
            {
                Signals = [.. next.Rationale.Signals,
                new("diversity", Diversity(next), "Source, topic, format and near-duplicate balance"),
                new("serendipity-slot", next.Outside ? 1 : 0, "Outside strongest lasting topics; contributes to the configured serendipity share"),
                new("selected-score", next.Score + Diversity(next), "Score at selection, after diversity adjustments")]
            }));
            if (next.Outside) outsideSelected++;
            var key = SourceKey(next.Item);
            sourceCounts[key] = sourceCounts.GetValueOrDefault(key) + 1;
            foreach (var id in next.Item.TopicIds) topicCounts[id] = topicCounts.GetValueOrDefault(id) + 1;
            kindCounts[next.Item.Kind] = kindCounts.GetValueOrDefault(next.Item.Kind) + 1;
        }
        return chosen;
    }

    public async Task<List<Recommendation>> RelatedAsync(ContentItem item, int count, CancellationToken ct, IReadOnlySet<Guid>? visited = null, bool includeVisited = false)
    {
        var all = await db.Items.AsNoTracking().Where(i => i.Id != item.Id).ToListAsync(ct);
        var states = await db.States.AsNoTracking().ToDictionaryAsync(s => s.ItemId, ct);
        var eligibleIds = await EligibleIds(all, states, ct);
        all = all.Where(i => eligibleIds.Contains(i.Id) && (includeVisited || visited?.Contains(i.Id) != true)).ToList();
        var topics = await db.Topics.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var seedConcepts = Concepts.Focus(Concepts.Match(item));
        var words = Tokenize(RelatedText(item));
        var titleWords = Tokenize(item.Title);
        var tokens = all.ToDictionary(i => i.Id, i => Tokenize(RelatedText(i)));
        var frequencies = tokens.Values.SelectMany(x => x).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        double Rarity(string word) => Math.Log(1 + (all.Count + 1.0) / (1 + frequencies.GetValueOrDefault(word)));
        var candidates = new List<RelatedCandidate>();
        foreach (var other in all)
        {
            var matches = Concepts.Focus(Concepts.Match(other));
            var shared = seedConcepts.Join(matches, c => c.Id, c => c.Id, (a, b) => (A: a, B: b)).ToList();
            var adjacent = (from a in seedConcepts from b in matches where Concepts.Adjacent(a.Id, b.Id) select (A: a, B: b)).ToList();
            var family = (from a in seedConcepts from b in matches where a.Id != b.Id && Concepts.Family(a.Id) is { } f && f == Concepts.Family(b.Id) select (A: a, B: b)).ToList();
            var sharedTopics = item.TopicIds.Intersect(other.TopicIds).Where(topics.ContainsKey).ToList();
            var sharedWords = words.Intersect(tokens[other.Id]).OrderByDescending(Rarity).ThenBy(x => x, StringComparer.Ordinal).ToList();
            var phrases = Phrases(item.Title).Intersect(Phrases(RelatedText(other))).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var format = FormatSignal(other);
            var tier = 0;
            var score = 0.0;
            string summary, direction, kind, detail;
            var evidence = new List<RecommendationEvidence>();
            var usedConcepts = new List<string>();
            if (shared.Count > 0 || adjacent.Count > 0 || family.Count > 0)
            {
                var pair = shared.OrderByDescending(p => p.A.Id is not ("robotics" or "uap-reports")).ThenByDescending(p => p.B.Field is "title" or "user-tag").FirstOrDefault();
                if (shared.Count > 0)
                {
                    tier = pair.A.Id is "robotics" or "uap-reports" ? 1 : 0;
                    score = (tier == 0 ? 6 : 4) + Math.Min(2, shared.Count - 1) * 0.5;
                    summary = $"Continue with {pair.A.Label}"; direction = "Close continuation"; kind = "shared-concept";
                }
                else
                {
                    pair = adjacent.Count > 0 ? adjacent.OrderBy(p => Concepts.EdgeOrder(p.A.Id, p.B.Id)).First() : family[0];
                    tier = adjacent.Count > 0 ? 1 : 2;
                    score = adjacent.Count > 0 ? 3.5 : 1.8;
                    if (adjacent.Count > 0) score -= Math.Min(4, Concepts.EdgeOrder(pair.A.Id, pair.B.Id)) * 0.25;
                    var cross = Concepts.Family(pair.A.Id) != Concepts.Family(pair.B.Id);
                    direction = cross ? "Across disciplines" : tier == 2 ? "Broader context" : "Adjacent subject";
                    kind = tier == 2 ? "concept-family" : "adjacent-concept";
                    summary = $"{pair.A.Label} → {pair.B.Label}";
                }
                if (pair.A.Field == "body" || pair.B.Field == "body")
                {
                    score *= 0.55; tier = Math.Max(tier, 2);
                    if (shared.Count > 0) { summary = $"Text connection: {pair.A.Label}"; direction = "Text connection"; }
                }
                score += pair.B.Field is "title" or "user-tag" ? 0.65 : pair.B.Field == "excerpt" ? 0.3 : 0;
                usedConcepts = matches.Select(m => m.Id).ToList();
                evidence.Add(new(item.Id, item.Title, "starting-item", pair.A.Label, pair.A.Field, pair.A.Match));
                evidence.Add(new(other.Id, other.Title, "candidate", pair.B.Label, pair.B.Field, pair.B.Match));
                detail = shared.Count > 0 ? "Both records name this concept in their available text or your tags." : "A suggested subject connection, not a claim that the sources agree or cite one another.";
            }
            else if (sharedWords.Count >= 2 && phrases.Count > 0 && (seedConcepts.Count == 0 || matches.Count == 0 && sharedWords.Count >= 3) && format.Value >= 0)
            {
                tier = 2; score = 1.4 + Math.Min(0.8, sharedWords.Sum(Rarity) / Math.Sqrt(Math.Max(1, words.Count) * Math.Max(1, tokens[other.Id].Count)));
                summary = "Shared subject phrase: " + string.Join("; ", phrases.Take(2)); direction = "Text connection"; kind = "phrase";
                detail = "Matching words in the available source text; no stronger concept relationship was recognized.";
                evidence.Add(new(item.Id, item.Title, "starting-item", Field: "title", Match: phrases[0]));
                evidence.Add(new(other.Id, other.Title, "candidate", Field: "source-text", Match: phrases[0]));
            }
            else if (sharedTopics.Count > 0 && format.Value >= 0)
            {
                tier = 3; score = 0.3; summary = $"Shared broad topic: {topics[sharedTopics[0]]}"; direction = "Broad topic"; kind = "broad-topic";
                detail = "No close concept match was found. This is a broader catalog suggestion.";
                evidence.Add(new(item.Id, item.Title, "starting-item", topics[sharedTopics[0]], "topic"));
            }
            else continue;
            var lexical = Math.Min(0.4, sharedWords.Count(w => titleWords.Contains(w)) * 0.08);
            var formatWeight = format.Value < 0 ? format.Value * (states.GetValueOrDefault(other.Id)?.Saved == true || states.GetValueOrDefault(other.Id)?.Feedback > 0 ? 1 : 6) : format.Value;
            var relationshipScore = score;
            score += lexical + formatWeight;
            candidates.Add(new(other, new RecommendationRationale
            {
                Kind = kind,
                Summary = summary,
                Direction = direction,
                Detail = detail,
                Evidence = evidence,
                Signals = [new("relationship-tier", tier, "0 shared concept; 1 adjacent subject; 2 context; 3 broad fallback"),
                    new("relationship", relationshipScore, summary), new("text-support", lexical, string.Join(", ", sharedWords.Take(5))), new("format", formatWeight, format.Detail)]
            }, score, tier, usedConcepts));
        }
        // Broaden only when few defensible matches exist; never fill twenty slots
        // merely because two records share an umbrella topic.
        var broadLimit = Math.Min(2, Math.Max(0, 4 - candidates.Count(c => c.Tier < 3)));
        var chosen = new List<Recommendation>();
        var conceptCounts = new Dictionary<string, int>();
        var sourceCounts = new Dictionary<string, int>();
        var directionCounts = new Dictionary<string, int>();
        var broadSelected = 0;
        while (chosen.Count < count && candidates.Count > 0)
        {
            var eligible = candidates.Where(c => c.Tier < 3 || broadSelected < broadLimit).ToList();
            if (eligible.Count == 0) break;
            double Diversity(RelatedCandidate c) => -Math.Min(2.4, sourceCounts.GetValueOrDefault(SourceKey(c.Item)) * 0.8)
                - Math.Min(2.4, c.Concepts.Select(id => conceptCounts.GetValueOrDefault(id) * 0.85).DefaultIfEmpty(0).Max())
                - Math.Min(2.1, directionCounts.GetValueOrDefault(c.Rationale.Direction ?? "") * 0.7)
                - (chosen.Any(x => SimilarTitles(x.Item, c.Item)) || SimilarTitles(item, c.Item) ? 3 : 0)
                - (visited?.Contains(c.Item.Id) == true ? 1 : 0);
            var next = eligible.OrderBy(c => c.Tier == 3).ThenByDescending(c => c.Score + Diversity(c)).ThenBy(c => c.Item.Id).First();
            candidates.Remove(next);
            if (next.Tier == 3) broadSelected++;
            chosen.Add(new(next.Item, next.Rationale with
            {
                Signals = [.. next.Rationale.Signals,
                new("diversity", Diversity(next), "Repeated source/concept, near-duplicate titles and revisits"),
                new("selected-score", next.Score + Diversity(next), "Score at selection, after diversity adjustments")]
            }, visited?.Contains(next.Item.Id) == true));
            var key = SourceKey(next.Item);
            sourceCounts[key] = sourceCounts.GetValueOrDefault(key) + 1;
            var direction = next.Rationale.Direction ?? "";
            directionCounts[direction] = directionCounts.GetValueOrDefault(direction) + 1;
            foreach (var concept in next.Concepts) conceptCounts[concept] = conceptCounts.GetValueOrDefault(concept) + 1;
        }
        return chosen;
    }

    private async Task<HashSet<Guid>> EligibleIds(List<ContentItem> items, Dictionary<Guid, ItemState> states, CancellationToken ct)
    {
        var sources = await db.Sources.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var memberships = (await db.ItemSources.AsNoTracking().ToListAsync(ct)).ToLookup(x => x.ItemId);
        return items.Where(item =>
        {
            var origins = memberships[item.Id].Where(x => x.SourceId.HasValue && sources.ContainsKey(x.SourceId.Value)).Select(x => sources[x.SourceId!.Value]).ToList();
            if (origins.Count == 0 && item.SourceId is Guid id && sources.TryGetValue(id, out var primary)) origins.Add(primary);
            return states.GetValueOrDefault(item.Id)?.Dismissed != true && (origins.Count == 0 || origins.Any(s => s.Enabled));
        }).Select(i => i.Id).ToHashSet();
    }
    private static double Interest(Topic topic) => topic.CuriosityOnly ? Math.Min(0.55, topic.Weight / 10.0) : topic.Weight / 2.5 + (topic.Pinned ? 0.5 : 0);
    public static (double Value, string Detail) FormatSignal(ContentItem item)
    {
        var title = item.Title.ToLowerInvariant();
        if (Regex.IsMatch(title, @"\b(servicing|maintenance release|patch tuesday|release notes|security updates|monthly roundup|call for papers|journal announcements|graduate showcase|staff appointments)\b|\bllm-[\w-]+\s+\d+\.\d+"))
            return (-0.45, "The title resembles a routine update or notice; a small ranking reduction, not a quality judgment.");
        if (Regex.IsMatch(title, @"\b(announcing|introducing|partnership|collaborate|our next era|responsibly and safely)\b"))
            return (-0.2, "The title resembles an announcement; specific interests and explicit feedback can outweigh this.");
        if (item.Kind == "paper" && (item.Excerpt?.Length ?? 0) > 180) return (0.15, "A research record with an available abstract.");
        if (Regex.IsMatch(title, @"\b(how|why|principles|tutorial|explained|guide|theory)\b") && (item.Excerpt?.Length ?? 0) > 100)
            return (0.1, "The title and available description suggest an explanatory piece.");
        return (0, "No format preference applied.");
    }
    private static readonly HashSet<string> StopWords = "the and for with this that from have what when where which their there about these those into your they been being would could should will than then also more most some such only other each both through across using used uses first new how why all our are can not but its his her who had has does may one two you was were after before under over between without within toward towards article post posted read reading subscribe newsletter copyright rights reserved appears appeared het bericht verscheen eerst foundation abstract paper study research introduction based results show presents work works approach time information encyclopedia blog latest version search project general report sheet model models release announces announcement introducing intelligence system systems test fixture".Split(' ').ToHashSet();
    private static HashSet<string> Tokenize(string? text) => Regex.Matches((text ?? "").ToLowerInvariant(), @"\p{L}[\p{L}\p{N}]{2,}").Select(m => m.Value).Where(x => !StopWords.Contains(x)).ToHashSet();
    private static string RelatedText(ContentItem item) => item.Title + " " + item.Excerpt + " " + ((item.Excerpt?.Length ?? 0) < 200 && item.Body is { } body ? body[..Math.Min(body.Length, 2500)] : "");
    private static HashSet<string> Phrases(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"\p{L}[\p{L}\p{N}]{2,}").Select(m => m.Value).ToArray();
        return words.Zip(words.Skip(1), (a, b) => (a, b)).Where(p => !StopWords.Contains(p.a) && !StopWords.Contains(p.b)).Select(p => p.a + " " + p.b).ToHashSet();
    }
    private static string SourceKey(ContentItem item) => item.SourceId?.ToString() ?? (Uri.TryCreate(item.CanonicalUrl, UriKind.Absolute, out var uri) ? uri.Host : item.Provider);
    private static bool SimilarTitles(ContentItem a, ContentItem b)
    {
        var left = Tokenize(a.Title); var right = Tokenize(b.Title);
        return left.Count >= 3 && right.Count >= 3 && left.Intersect(right).Count() / (double)left.Union(right).Count() >= 0.75;
    }
    private static uint StableHash(string value)
    {
        uint hash = 2166136261;
        foreach (var ch in value) hash = (hash ^ ch) * 16777619;
        return hash;
    }
    private sealed record DailyCandidate(ContentItem Item, RecommendationRationale Rationale, double Score, bool Outside);
    private sealed record RelatedCandidate(ContentItem Item, RecommendationRationale Rationale, double Score, int Tier, List<string> Concepts);
}
