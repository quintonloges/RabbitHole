using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Ingestion;

namespace RabbitHole.Features;

/// <summary>Allowlisted public fields only. Never extend ItemDto or serialize a persistence record.</summary>
public sealed record PublicTopicDto(Guid Id, string Name);
public sealed record PublicItemDto(Guid Id, string Title, string CanonicalUrl, string? Author,
    DateTimeOffset? PublishedAt, DateTimeOffset IngestedAt, string Type, string Provider,
    string SourceName, string? Excerpt, List<PublicTopicDto> Topics, string? EmbedUrl, string? ImageUrl);
public sealed record PublicCatalogPage(List<PublicItemDto> Items, int Total, int Page, int PageSize);

public static class PublicCatalogPolicy
{
    internal static bool IsDocumentMetadata(string kind, string provider, string provenance) =>
        kind.ToLowerInvariant() is "book" or "document" or "pdf" or "epub" or "chapter" or "section" ||
        provider.ToLowerInvariant() is "local" or "local-upload" or "document" or "upload" or "pdf" or "epub" ||
        provenance.Equals("personal-upload", StringComparison.OrdinalIgnoreCase);

    public static bool IsEligible(ContentItem item) => item.DocumentId == null && item.SectionId == null &&
        !IsDocumentMetadata(item.Kind, item.Provider, item.TextProvenance) && SafeUrl(item.CanonicalUrl) != null;

    private static bool IsPrivateAppPath(string value)
    {
        var path = value;
        for (var iteration = 0; iteration < 3; iteration++) path = Uri.UnescapeDataString(path);
        path = path.Replace('\\', '/');
        while (path.Contains("//", StringComparison.Ordinal)) path = path.Replace("//", "/", StringComparison.Ordinal);
        path = new Uri("https://public.example" + path).AbsolutePath;
        return new[] { "/admin", "/api/documents", "/api/library", "/api/items", "/api/holes", "/api/practice",
            "/api/export", "/api/settings", "/api/sources", "/api/ai", "/api/auth", "/api/bootstrap", "/api/discover", "/api/system" }
            .Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string DecodeBounded(string value)
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var decoded = Uri.UnescapeDataString(value);
            if (decoded == value) break;
            value = decoded;
        }
        return value;
    }

    private static bool SafeParameterValue(string value, int depth)
    {
        value = DecodeBounded(value).Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(value, "%[0-9a-fA-F]{2}")) return false;
        if (value.StartsWith("//", StringComparison.Ordinal)) value = "https:" + value;
        if (value.Contains("://", StringComparison.Ordinal))
            return SafeUrl(value, depth + 1) != null;
        // Encoded query fragments must not smuggle credentials without a URL wrapper.
        var query = value.Contains('?') ? value[(value.IndexOf('?') + 1)..] : value.TrimStart('#');
        return !query.Contains('=') || !QueryHelpers.ParseQuery(query).Keys.Any(CredentialKeys.IsSecret);
    }

    public static string? SafeUrl(string? value) => SafeUrl(value, 0);

    private static string? SafeUrl(string? value, int depth)
    {
        if (depth > 4 || string.IsNullOrWhiteSpace(value) || value.Length > 4096) return null;
        try
        {
            var uri = SafeRemoteFetcher.ValidateUrl(value);
            if (!uri.Host.Contains('.') && uri.HostNameType != UriHostNameType.IPv6) return null;
            if (new[] { ".lan", ".home", ".home.arpa", ".localdomain", ".invalid" }.Any(suffix => uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) return null;
            if (IsPrivateAppPath(uri.AbsolutePath)) return null;
            if (!SafeParameterValue(uri.Fragment, depth)) return null;
            var query = QueryHelpers.ParseQuery(uri.Query);
            if (query.Keys.Any(CredentialKeys.IsSecret) || query.Values.SelectMany(values => values).Any(parameter => !SafeParameterValue(parameter ?? "", depth))) return null;
            // Fragments may carry OAuth tokens or private document locators.
            return new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
        }
        catch (Exception error) when (error is RemoteFetchException or UriFormatException) { return null; }
    }

    public static string? SafeEmbed(string? value)
    {
        var safe = SafeUrl(value);
        if (safe == null || !ProviderAdapters.IsApprovedEmbed(safe)) return null;
        var uri = new Uri(safe);
        if (uri.Host == "www.youtube-nocookie.com")
            return new UriBuilder(uri) { Query = "", Fragment = "" }.Uri.AbsoluteUri;
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.TryGetValue("url", out var target) || SafeUrl(target.ToString()) is not { } track ||
            !ProviderAdapters.IsSoundCloudHost(new Uri(track).Host)) return null;
        return "https://w.soundcloud.com/player/?url=" + Uri.EscapeDataString(track);
    }

    // Includes section references even if a legacy or corrupt record lost its DocumentId.
    public static async Task<HashSet<Guid>> DocumentDerivedIdsAsync(AppDbContext db, CancellationToken ct)
    {
        var records = await db.Items.IgnoreQueryFilters().AsNoTracking()
            .Select(i => new { i.Id, i.MergedIntoId, i.DocumentId, i.SectionId, i.Kind, i.Provider, i.TextProvenance }).ToListAsync(ct);
        var blocked = (await db.Sections.AsNoTracking().Select(s => s.ItemId).ToListAsync(ct)).ToHashSet();
        foreach (var i in records)
            if (i.DocumentId.HasValue || i.SectionId.HasValue ||
                IsDocumentMetadata(i.Kind, i.Provider, i.TextProvenance)) blocked.Add(i.Id);
        // A document-derived survivor, its aliases, and any survivor reached from a
        // document alias are all private. Iterate to close the entire alias component.
        bool changed;
        do
        {
            changed = false;
            foreach (var i in records.Where(i => i.MergedIntoId.HasValue))
                if (blocked.Contains(i.Id) || blocked.Contains(i.MergedIntoId!.Value))
                { changed |= blocked.Add(i.Id); changed |= blocked.Add(i.MergedIntoId!.Value); }
        } while (changed);
        return blocked;
    }

    public static async Task<bool> CanPublishAsync(AppDbContext db, ContentItem item, CancellationToken ct) =>
        IsEligible(item) && await db.PublicCatalogEntries.AnyAsync(i => i.Id == item.Id, ct);
}

public static class PublicCatalog
{
    public static IQueryable<PublicCatalogEntry> Visible(AppDbContext db) =>
        db.PublicCatalogEntries.AsNoTracking().Where(i => i.IsPublic && i.MergedIntoId == null);

    public static async Task<PublicCatalogEntry?> ResolveAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        while (seen.Add(id))
        {
            var current = await db.PublicCatalogEntries.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && i.IsPublic, ct);
            if (current == null || current.MergedIntoId == null) return current;
            id = current.MergedIntoId.Value;
        }
        return null;
    }

    public static async Task<List<PublicItemDto>> BuildAsync(AppDbContext db, IEnumerable<PublicCatalogEntry> records, CancellationToken ct)
    {
        var list = records.ToList();
        var ids = list.Select(i => i.Id).ToList();
        var topics = (await db.PublicCatalogTopics.AsNoTracking().Where(t => ids.Contains(t.ItemId)).OrderBy(t => t.Name).ToListAsync(ct))
            .ToLookup(t => t.ItemId, t => new PublicTopicDto(t.TopicId, t.Name));
        return list.Select(i => new PublicItemDto(i.Id, i.Title, i.CanonicalUrl, i.Author, i.PublishedAt,
            i.IngestedAt, i.Type, i.Provider, i.SourceName, i.Excerpt, topics[i.Id].ToList(), i.EmbedUrl, i.ImageUrl)).ToList();
    }
}
