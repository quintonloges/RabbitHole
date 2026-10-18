using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;

namespace RabbitHole.Data;

public static class ItemCatalog
{
    public static List<ItemIdentity> Identities(string url, string provider, string? providerId,
        string? doi, string? arxiv, Guid? sourceId, int sourceVersion = 0)
    {
        var identities = new List<ItemIdentity>();
        void Add(string kind, string scope, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            identities.Add(new ItemIdentity
            {
                Kind = kind,
                Scope = scope,
                Value = value,
                Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\n" + scope + "\n" + value)))
            });
        }
        Add("url", "", url);
        Add("doi", "", ProviderAdapters.NormalizeDoi(doi));
        Add("arxiv", "", arxiv?.ToLowerInvariant());
        // An RSS GUID is unique only inside its feed. Unknown adapters also need a source scope.
        var scope = provider is "openalex" or "arxiv" or "youtube" or "soundcloud" ? "" : sourceId?.ToString() ?? url;
        if (sourceId.HasValue && scope.Length > 0 && sourceVersion > 0) scope += ":v" + sourceVersion;
        Add("provider:" + provider.ToLowerInvariant(), scope, providerId);
        return identities.DistinctBy(x => x.Key).ToList();
    }

    public static async Task<ContentItem?> ResolveAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        while (seen.Add(id))
        {
            var item = await db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (item?.MergedIntoId is not Guid next) return item;
            id = next;
        }
        throw new InvalidOperationException("Circular item alias.");
    }

    public static async Task<Dictionary<Guid, Guid>> ResolveIdsAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Guid>();
        foreach (var id in ids.Distinct())
            if (await ResolveAsync(db, id, ct) is { } item) result[id] = item.Id;
        return result;
    }

    public static async Task AttachSourceAsync(AppDbContext db, ContentItem item, Guid? sourceId, CancellationToken ct)
    {
        if (sourceId is not Guid id || await db.Sources.FindAsync([id], ct) is not { } source) return;
        var link = db.ItemSources.Local.FirstOrDefault(x => x.ItemId == item.Id && x.SourceKey == id && x.SourceVersion == source.IdentityVersion)
            ?? await db.ItemSources.FirstOrDefaultAsync(x => x.ItemId == item.Id && x.SourceKey == id && x.SourceVersion == source.IdentityVersion, ct);
        if (link is null)
        {
            link = new ItemSource { ItemId = item.Id, SourceId = id, SourceKey = id, SourceVersion = source.IdentityVersion };
            db.ItemSources.Add(link);
        }
        link.Name = source.Name; link.Kind = source.Kind; link.Url = source.Url;
        link.TopicIds = source.TopicIds.ToList(); link.LastSeenAt = DateTimeOffset.UtcNow;
        item.SourceId ??= id;
    }

    public static async Task RegisterAsync(AppDbContext db, ContentItem item, IEnumerable<ItemIdentity> identities, CancellationToken ct)
    {
        foreach (var identity in identities)
        {
            var existing = db.ItemIdentities.Local.FirstOrDefault(x => x.Key == identity.Key)
                ?? await db.ItemIdentities.FindAsync([identity.Key], ct);
            if (existing is not null) existing.ItemId = item.Id;
            else { identity.ItemId = item.Id; db.ItemIdentities.Add(identity); }
        }
    }

    public static async Task<ContentItem> MergeAsync(AppDbContext db, IEnumerable<ContentItem> candidates, CancellationToken ct)
    {
        var ordered = candidates.Where(x => x.MergedIntoId == null).DistinctBy(x => x.Id)
            .OrderBy(x => x.IngestedAt).ThenBy(x => x.Id).ToList();
        var winner = ordered.First();
        foreach (var loser in ordered.Skip(1))
        {
            if (winner.DocumentId.HasValue || loser.DocumentId.HasValue)
                throw new InvalidOperationException("Local document sections cannot merge with external records.");
            await AttachSourceAsync(db, winner, winner.SourceId, ct);
            await AttachSourceAsync(db, loser, loser.SourceId, ct);
            await TopicClassification.AssignAsync(db, winner, [], ct);
            await TopicClassification.AssignAsync(db, loser, [], ct);
            var winnerTopics = db.ItemTopics.Local.Where(x => x.ItemId == winner.Id).ToDictionary(x => x.TopicId);
            foreach (var edge in db.ItemTopics.Local.Where(x => x.ItemId == loser.Id).ToList())
            {
                if (!winnerTopics.TryGetValue(edge.TopicId, out var target))
                {
                    target = new ItemTopic { ItemId = winner.Id, TopicId = edge.TopicId };
                    db.ItemTopics.Add(target); winnerTopics[edge.TopicId] = target;
                }
                target.LegacyAssigned |= edge.LegacyAssigned;
                if (edge.UserUpdatedAt is not null && (target.UserUpdatedAt is null || edge.UserUpdatedAt > target.UserUpdatedAt))
                { target.UserOverride = edge.UserOverride; target.UserUpdatedAt = edge.UserUpdatedAt; }
            }
            var state = await db.States.FindAsync([winner.Id], ct);
            var oldState = await db.States.FindAsync([loser.Id], ct);
            if (oldState is not null)
            {
                if (state is null) { state = new ItemState { ItemId = winner.Id, Dismissed = oldState.Dismissed }; db.States.Add(state); }
                state.Saved |= oldState.Saved; state.Read |= oldState.Read; state.Understood |= oldState.Understood;
                state.Dismissed &= oldState.Dismissed; state.Progress = Math.Max(state.Progress, oldState.Progress);
                state.SavedAt = Later(state.SavedAt, oldState.SavedAt); state.ReadAt = Later(state.ReadAt, oldState.ReadAt);
                state.LastOpenedAt = Later(state.LastOpenedAt, oldState.LastOpenedAt);
                if (state.Feedback == 0) state.Feedback = oldState.Feedback;
                // Retain the alias's original state as an exportable historical snapshot.
            }
            var links = await db.ItemSources.Where(x => x.ItemId == loser.Id).ToListAsync(ct);
            links = links.Concat(db.ItemSources.Local.Where(x => x.ItemId == loser.Id)).Distinct().ToList();
            foreach (var link in links)
            {
                var target = db.ItemSources.Local.FirstOrDefault(x => x.ItemId == winner.Id && x.SourceKey == link.SourceKey && x.SourceVersion == link.SourceVersion)
                    ?? await db.ItemSources.FirstOrDefaultAsync(x => x.ItemId == winner.Id && x.SourceKey == link.SourceKey && x.SourceVersion == link.SourceVersion, ct);
                if (target is null) link.ItemId = winner.Id;
                else { target.LastSeenAt = Later(target.LastSeenAt, link.LastSeenAt)!.Value; db.ItemSources.Remove(link); }
            }
            foreach (var identity in await db.ItemIdentities.Where(x => x.ItemId == loser.Id).ToListAsync(ct)) identity.ItemId = winner.Id;
            foreach (var identity in db.ItemIdentities.Local.Where(x => x.ItemId == loser.Id)) identity.ItemId = winner.Id;
            foreach (var note in await db.Notes.Where(x => x.ItemId == loser.Id).ToListAsync(ct)) note.ItemId = winner.Id;
            foreach (var node in await db.Nodes.Where(x => x.ItemId == loser.Id).ToListAsync(ct)) node.ItemId = winner.Id;
            foreach (var prompt in await db.Prompts.Where(x => x.ItemId == loser.Id).ToListAsync(ct)) prompt.ItemId = winner.Id;
            foreach (var result in await db.AiResults.Where(x => x.ItemId == loser.Id).ToListAsync(ct)) result.ItemId = winner.Id;
            // Feed entries and evidence arrays are historical references; resolve their old IDs on read.
            winner.SourceId ??= loser.SourceId; winner.Author ??= loser.Author;
            winner.Excerpt ??= loser.Excerpt;
            if (winner.Body is null && loser.Body is not null) { winner.Body = loser.Body; winner.TextProvenance = loser.TextProvenance; }
            winner.Doi ??= loser.Doi; winner.ArxivId ??= loser.ArxivId;
            winner.PublishedAt ??= loser.PublishedAt; winner.ImageUrl ??= loser.ImageUrl; winner.EmbedUrl ??= loser.EmbedUrl;
            winner.MetadataJson = MergeMetadata(winner.MetadataJson, loser.MetadataJson, winner.Provider, winner.ProviderId, loser.Provider, loser.ProviderId);
            loser.MergedIntoId = winner.Id;
        }
        await TopicClassification.AssignAsync(db, winner, [], ct);
        return winner;
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) => a is null ? b : b is null ? a : a > b ? a : b;

    public static string? MergeMetadata(string? previous, string? current, string oldProvider, string? oldId, string newProvider, string? newId)
    {
        static JsonObject Parse(string? json) { try { return JsonNode.Parse(json ?? "{}") as JsonObject ?? new(); } catch (System.Text.Json.JsonException) { return new(); } }
        var root = Parse(previous);
        var incoming = Parse(current);
        foreach (var property in incoming.Where(x => x.Key != "providerReferences"))
            if (!root.ContainsKey(property.Key)) root[property.Key] = property.Value?.DeepClone();
        var refs = root["providerReferences"] as JsonArray ?? new JsonArray();
        void Add(string provider, string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (!refs.OfType<JsonObject>().Any(x => x["provider"]?.ToString() == provider && x["id"]?.ToString() == id))
                refs.Add(new JsonObject { ["provider"] = provider, ["id"] = id });
        }
        if (incoming["providerReferences"] is JsonArray incomingRefs)
            foreach (var reference in incomingRefs.OfType<JsonObject>()) Add(reference["provider"]?.ToString() ?? "", reference["id"]?.ToString());
        Add(oldProvider, oldId); Add(newProvider, newId);
        if (refs.Parent is null && refs.Count > 0) root["providerReferences"] = refs;
        return root.Count == 0 ? null : root.ToJsonString();
    }
}
