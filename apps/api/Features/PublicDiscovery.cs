using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;

namespace RabbitHole.Features;

public static class PublicDiscovery
{
    public static async Task<List<PublicCatalogEntry>> PageAsync(IQueryable<PublicCatalogEntry> visible,
        int total, int offset, int size, DateOnly day, CancellationToken ct)
    {
        if (size <= 0 || offset >= total) return [];
        var cutoff = day.AddDays(-ArchiveDiscovery.MinimumAgeDays).DayNumber;
        var multiplier = ArchiveDiscovery.RotationKey("public-archive:" + day.ToString("yyyy-MM-dd"));
        // Only compact scheduling fields from the already-filtered public index.
        // No private tables, body text, preferences, cached visibility or writes.
        var metadata = await visible.Select(i => new
        {
            i.Id,
            i.CanonicalUrl,
            i.SourceKey,
            i.Author,
            Date = i.PublishedAt ?? i.IngestedAt,
            i.IngestedAt,
            Rotation = i.RotationKey * multiplier % ArchiveDiscovery.RotationModulus,
            Archive = i.ArchiveCandidate && (i.PublishedDay != null && i.PublishedDay <= cutoff ||
                i.PublishedDay == null && i.Provider == "web-archive") && i.Topics.Any(),
            Topics = i.Topics.Select(t => t.TopicId).ToList()
        }).ToListAsync(ct);
        // Each lane has the same publisher, topics and age stream. Only its head
        // can win, avoiding a full-catalog scan for every selection.
        var lanes = metadata.Select(i => new Candidate(i.Id,
                DiscoveryBalance.Publisher(i.CanonicalUrl, i.SourceKey, i.Author),
                i.Topics.Order().ToArray(), i.Archive, i.Date, i.IngestedAt, i.Rotation))
            .GroupBy(i => (i.Publisher, i.Archive, Topics: string.Join(',', i.Topics)))
            .Select(g => new Queue<Candidate>(g.OrderBy(i => i.Archive ? i.Rotation : 0)
                .ThenByDescending(i => i.Archive ? DateTimeOffset.MinValue : i.Date)
                .ThenByDescending(i => i.Archive ? DateTimeOffset.MinValue : i.IngestedAt).ThenBy(i => i.Id)))
            .ToList();
        var balance = new DiscoveryBalance();
        var selected = new List<Guid>();
        // Replay the same prefix for every page size; page boundaries never reset
        // budgets or change earlier choices. Pagination remains complete.
        for (var position = 0; position < Math.Min(total, (long)offset + size) && lanes.Count > 0; position++)
        {
            ct.ThrowIfCancellationRequested();
            var available = balance.SpreadPublishers(lanes, lane => lane.Peek().Publisher);
            var stream = available.Where(lane => lane.Peek().Archive == (position % 4 == 3)).ToList();
            if (stream.Count > 0) available = stream;
            var nextLane = available.OrderBy(lane => balance.TopicLoad(lane.Peek().Topics))
                .ThenBy(lane => balance.PublisherCount(lane.Peek().Publisher))
                .ThenBy(lane => lane.Peek().Archive ? lane.Peek().Rotation : 0)
                .ThenByDescending(lane => lane.Peek().Archive ? DateTimeOffset.MinValue : lane.Peek().Date)
                .ThenByDescending(lane => lane.Peek().Archive ? DateTimeOffset.MinValue : lane.Peek().IngestedAt)
                .ThenBy(lane => lane.Peek().Id).First();
            var next = nextLane.Dequeue();
            balance.Add(next.Publisher, next.Topics);
            if (position >= offset) selected.Add(next.Id);
            if (nextLane.Count == 0) lanes.Remove(nextLane);
        }
        // Materialize full public records only for this page. Reapply visibility
        // so a concurrent hide cannot leak from the compact scheduling plan.
        var records = await visible.Where(i => selected.Contains(i.Id)).Take(size).ToDictionaryAsync(i => i.Id, ct);
        return selected.Where(records.ContainsKey).Select(id => records[id]).ToList();
    }

    private sealed record Candidate(Guid Id, string Publisher, Guid[] Topics, bool Archive,
        DateTimeOffset Date, DateTimeOffset IngestedAt, long Rotation);
}
