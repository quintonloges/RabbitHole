using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;

namespace RabbitHole.Ingestion;

/// <summary>Finds previously unimported original links in the HN Search index.
/// Submission dates and submitter identities are never article publication/author metadata.</summary>
public sealed class WebArchiveDiscovery(AppDbContext db, IRemoteFetcher fetcher,
    Func<string, string?, CancellationToken, Task<NormalizedItem>> metadata)
{
    public static Uri SearchUrl(string query, DateTimeOffset before, DateTimeOffset? after = null, int page = 0)
    {
        var filter = "created_at_i<" + before.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        if (after != null) filter += ",created_at_i>=" + after.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return new("https://hn.algolia.com/api/v1/search_by_date?tags=story&restrictSearchableAttributes=title&query=" +
            Uri.EscapeDataString(query) + "&numericFilters=" + Uri.EscapeDataString(filter) + "&hitsPerPage=20&page=" + Math.Clamp(page, 0, 49));
    }

    public async Task<FetchBatch> FetchAsync(Source source, CancellationToken ct, DateTimeOffset? asOf = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var now = asOf ?? DateTimeOffset.UtcNow;
        var seed = ArchiveDiscovery.RotationKey(source.Query + ":" + now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var year = 2007 + (int)(seed % Math.Max(1, now.Year - 2007));
        var start = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddYears(1);
        var cutoff = now.AddDays(-ArchiveDiscovery.MinimumAgeDays);
        if (end > cutoff) end = cutoff;
        async Task<JsonDocument> Search(Uri url) => JsonDocument.Parse((await fetcher.FetchAsync(new RemoteRequest(url, ProviderAdapters.JsonTypes, 500_000), token)).Body);
        using var first = await Search(SearchUrl(source.Query, end, start));
        var pages = first.RootElement.TryGetProperty("nbPages", out var count) && count.TryGetInt32(out var n) ? Math.Clamp(n, 0, 50) : 1;
        var page = pages > 0 ? (int)(seed / 31 % pages) : 0;
        // Empty historical windows fall back to all older submissions; otherwise
        // sample a bounded page within that year. At most two index requests.
        using var second = pages == 0 ? await Search(SearchUrl(source.Query, cutoff))
            : page > 0 ? await Search(SearchUrl(source.Query, end, start, page)) : null;
        var root = (second ?? first).RootElement;
        if (!root.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            throw new FormatException("The web archive returned no valid results list.");
        var candidates = new List<(string Url, string Title, string Id, long SharedAt)>();
        foreach (var hit in hits.EnumerateArray().Take(20))
        {
            var url = String(hit, "url"); var title = String(hit, "title"); var id = String(hit, "objectID");
            if (url == null || title == null || id == null || !Regex.IsMatch(id, @"^\d{1,20}$") ||
                !hit.TryGetProperty("created_at_i", out var shared) || !shared.TryGetInt64(out var timestamp) ||
                timestamp < 0 || timestamp >= cutoff.ToUnixTimeSeconds()) continue;
            if (PublicCatalogPolicy.SafeUrl(url) is not { } safe || new Uri(safe).Host == "news.ycombinator.com" ||
                ArchiveDiscovery.IsTimeSensitive(title, null)) continue;
            candidates.Add((safe, ProviderAdapters.Plain(title, 400), id, timestamp));
        }
        var urls = candidates.Select(c => c.Url).ToList();
        var known = (await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => urls.Contains(i.CanonicalUrl)).Select(i => i.CanonicalUrl).ToListAsync(token)).ToHashSet();
        var result = new List<NormalizedItem>();
        foreach (var hit in candidates.DistinctBy(c => c.Url).Where(c => !known.Contains(c.Url))
            .OrderBy(c => ArchiveDiscovery.RotationKey(seed + ":" + c.Id)).Take(Math.Clamp(source.ResultLimit, 1, 3)))
        {
            var item = await metadata(hit.Url, null, token);
            // A permission-limited link may still be useful in the reader's browser;
            // a confirmed missing page should not enter the discovery catalog.
            if (Regex.IsMatch(item.Warning ?? "", @"\bHTTP (404|410)\b")) continue;
            if (item.PublishedAt > cutoff || ArchiveDiscovery.IsTimeSensitive(item.Title, item.Excerpt)) continue;
            var original = new Uri(hit.Url);
            if (item.Warning != null || item.Title == original.Host + original.AbsolutePath)
            {
                // HN's optional title year is not verified publication evidence.
                item.Title = Regex.Replace(hit.Title, @"\s*[\[(]\d{4}[\])]\s*$", "");
            }
            item.Provider = "web-archive"; item.ProviderId = hit.Id;
            item.MetadataJson = ItemCatalog.MergeMetadata(item.MetadataJson, JsonSerializer.Serialize(new
            {
                discoveredVia = "https://news.ycombinator.com/item?id=" + hit.Id,
                discoveryProvider = "Hacker News Search (Algolia)",
                discoveryTitle = hit.Title,
                sharedAt = DateTimeOffset.FromUnixTimeSeconds(hit.SharedAt),
                discoveryQuery = source.Query
            }), item.Provider, item.ProviderId, item.Provider, item.ProviderId);
            result.Add(item);
        }
        return new FetchBatch(result);
    }

    private static string? String(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
