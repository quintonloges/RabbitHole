using Microsoft.EntityFrameworkCore;
using RabbitHole.Domain;

namespace RabbitHole.Data;

public static class RecommendationUpgrade
{
    public const string VersionKey = "recommendation-provenance-v1";
    // Audited creation script and IDs in docs/feed-curation-report.md. These are
    // provenance repairs, never recommendation rules. Titles alone are not proof.
    public static readonly Guid[] CuratedTrailIds = [
        Guid.Parse("0cd21a8f-9914-4775-bf31-5c241c0019fa"),
        Guid.Parse("0780c86f-cd9c-473c-b5d8-fab922620dbe")
    ];
    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Settings.AnyAsync(s => s.Key == VersionKey, ct)) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var holes = await db.Holes.Where(h => CuratedTrailIds.Contains(h.Id) && h.Origin == "legacy-unknown").ToListAsync(ct);
        var ids = holes.Select(h => h.Id).ToList();
        foreach (var hole in holes)
        {
            hole.Origin = "curated";
            hole.OriginNote = "Created during the 17 October 2026 curation pass; docs/feed-curation-report.md.";
        }
        // Only the original ten nodes are known to be curated. Later branches keep
        // their own provenance, even if the trail itself started as an example.
        var cutoff = new DateTimeOffset(2026, 10, 18, 2, 52, 5, TimeSpan.Zero);
        foreach (var node in await db.Nodes.Where(n => ids.Contains(n.HoleId) && n.Origin == "legacy-unknown").ToListAsync(ct))
            if (node.CreatedAt <= cutoff) node.Origin = "curated";
        // Retain the earlier selection and every entry, but stop serving unrecorded
        // personalization as today's rationale. The next normal GET creates a stable
        // current edition. Older editions and their evidence remain historical data.
        var day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var edition = await db.Editions.SingleOrDefaultAsync(e => e.Day == day, ct);
        if (edition != null && await db.Entries.AnyAsync(e => e.EditionId == edition.Id && e.RationaleJson == null, ct))
            edition.Day = day + ":before-recommendation-evidence:" + edition.Id;
        db.Settings.Add(new AppSetting { Key = VersionKey, Value = "1" });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
