using Microsoft.EntityFrameworkCore;
using RabbitHole.Domain;
using RabbitHole.Features;

namespace RabbitHole.Data;

/// <summary>One-time backfill; the marker prevents future startups from undoing manual hides.</summary>
public static class PublicationUpgrade
{
    public const string VersionKey = "public-catalog-v1-backfilled";

    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Settings.AnyAsync(s => s.Key == VersionKey, ct)) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var blocked = await PublicCatalogPolicy.DocumentDerivedIdsAsync(db, ct);
        var records = await db.Items.IgnoreQueryFilters().ToListAsync(ct);
        foreach (var item in records)
            item.IsPublic = !blocked.Contains(item.Id) && PublicCatalogPolicy.IsEligible(item);
        // Restrictive visibility applies across every retained identity, including old aliases.
        bool changed;
        do
        {
            changed = false;
            var byId = records.ToDictionary(i => i.Id);
            foreach (var alias in records.Where(i => i.MergedIntoId.HasValue))
                if (byId.TryGetValue(alias.MergedIntoId!.Value, out var target) && (!alias.IsPublic || !target.IsPublic))
                { changed |= alias.IsPublic || target.IsPublic; alias.IsPublic = false; target.IsPublic = false; }
        } while (changed);
        db.Settings.Add(new AppSetting { Key = VersionKey, Value = "1" });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
