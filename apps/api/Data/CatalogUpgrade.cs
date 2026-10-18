using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Domain;
using RabbitHole.Features;

namespace RabbitHole.Data;

/// <summary>Idempotent, transactional backfill after the additive catalog migration.</summary>
public static class CatalogUpgrade
{
    public const string VersionKey = "catalog-v2-backfilled";

    public static List<ItemIdentity> RecordIdentities(ContentItem item)
    {
        var identities = ItemCatalog.Identities(item.CanonicalUrl, item.Provider, item.ProviderId, item.Doi, item.ArxivId, item.SourceId);
        // Older versions kept alternate global provider IDs in metadata. Do not guess the feed
        // namespace of legacy RSS references whose origin was never recorded.
        try
        {
            if (JsonNode.Parse(item.MetadataJson ?? "{}") is JsonObject metadata && metadata["providerReferences"] is JsonArray refs)
                foreach (var reference in refs.OfType<JsonObject>())
                {
                    var provider = reference["provider"]?.ToString();
                    if (provider is "openalex" or "arxiv" or "youtube" or "soundcloud")
                        identities.AddRange(ItemCatalog.Identities(item.CanonicalUrl, provider,
                            reference["id"]?.ToString(), null, null, null));
                }
        }
        catch (System.Text.Json.JsonException) { }
        return identities.DistinctBy(x => x.Key).ToList();
    }

    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Settings.AnyAsync(x => x.Key == VersionKey, ct)) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var originals = (await db.Items.ToListAsync(ct)).OrderBy(x => x.IngestedAt).ThenBy(x => x.Id).ToList();
        foreach (var original in originals)
        {
            if (original.MergedIntoId.HasValue) continue;
            var identities = RecordIdentities(original);
            var keys = identities.Select(x => x.Key).ToList();
            var ids = (await db.ItemIdentities.Where(x => keys.Contains(x.Key)).Select(x => x.ItemId).ToListAsync(ct))
                .Concat(db.ItemIdentities.Local.Where(x => keys.Contains(x.Key)).Select(x => x.ItemId)).Distinct().ToList();
            var candidates = new List<ContentItem> { original };
            foreach (var id in ids)
                if (await ItemCatalog.ResolveAsync(db, id, ct) is { } match) candidates.Add(match);
            var survivor = await ItemCatalog.MergeAsync(db, candidates, ct);
            await ItemCatalog.AttachSourceAsync(db, survivor, original.SourceId, ct);
            await ItemCatalog.RegisterAsync(db, survivor, identities, ct);
            await TopicClassification.AssignAsync(db, survivor, [], ct);
            await db.SaveChangesAsync(ct);
        }
        db.Settings.Add(new AppSetting { Key = VersionKey, Value = "1" });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
