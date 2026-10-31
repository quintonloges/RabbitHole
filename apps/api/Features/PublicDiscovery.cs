using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;

namespace RabbitHole.Features;

public static class PublicDiscovery
{
    // Two disjoint streams keep pagination bounded and duplicate-free. Visibility
    // and requested filters have already been applied to the supplied read model.
    public static async Task<List<PublicCatalogEntry>> PageAsync(IQueryable<PublicCatalogEntry> visible,
        int total, int offset, int size, DateOnly day, CancellationToken ct)
    {
        var cutoff = day.AddDays(-ArchiveDiscovery.MinimumAgeDays).DayNumber;
        var partitioned = visible.Select(i => new
        {
            Item = i,
            Archive = i.ArchiveCandidate && (i.PublishedDay != null && i.PublishedDay <= cutoff ||
                i.PublishedDay == null && i.Provider == "web-archive") && i.Topics.Any()
        });
        var archive = partitioned.Where(i => i.Archive).Select(i => i.Item);
        var recent = partitioned.Where(i => !i.Archive).Select(i => i.Item);
        var archiveCount = await archive.CountAsync(ct);
        var recentCount = total - archiveCount;
        var archiveSkip = Math.Min(archiveCount, Math.Max(offset / 4, offset - recentCount));
        var recentSkip = Math.Min(recentCount, offset - archiveSkip);
        var archiveUsed = archiveSkip;
        var recentUsed = recentSkip;
        var slots = new List<bool>();
        for (var position = offset; position < Math.Min(total, offset + size); position++)
        {
            var fromArchive = archiveUsed < archiveCount && (position % 4 == 3 || recentUsed >= recentCount);
            slots.Add(fromArchive);
            if (fromArchive) archiveUsed++; else recentUsed++;
        }
        // A daily seeded permutation computed in SQL. Products fit in Int64 on
        // both PostgreSQL and SQLite. Reloads use the same seed; no cached visibility.
        var multiplier = ArchiveDiscovery.RotationKey("public-archive:" + day.ToString("yyyy-MM-dd"));
        var older = new Queue<PublicCatalogEntry>(await archive
            .OrderBy(i => i.RotationKey * multiplier % ArchiveDiscovery.RotationModulus).ThenBy(i => i.Id)
            .Skip(archiveSkip).Take(archiveUsed - archiveSkip).ToListAsync(ct));
        var newer = new Queue<PublicCatalogEntry>(await recent
            .OrderByDescending(i => i.PublishedAt ?? i.IngestedAt).ThenByDescending(i => i.IngestedAt).ThenBy(i => i.Id)
            .Skip(recentSkip).Take(recentUsed - recentSkip).ToListAsync(ct));
        return slots.Select(fromArchive => fromArchive ? older.Dequeue() : newer.Dequeue()).ToList();
    }
}
