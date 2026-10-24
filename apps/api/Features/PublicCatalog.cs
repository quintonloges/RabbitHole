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
    private static readonly HashSet<string> CredentialKeys = new(StringComparer.OrdinalIgnoreCase)
    { "key", "api_key", "apikey", "access_token", "token", "auth", "authorization", "password", "passwd", "secret", "signature", "sig", "credential", "credentials", "client_secret", "code", "jwt", "refresh_token", "auth_token", "session", "sessionid", "session_id", "ticket", "secret_token", "oauth_token", "private_token" };

    private static bool IsDocumentMetadata(string kind, string provider, string provenance) =>
        kind.ToLowerInvariant() is "book" or "document" or "pdf" or "epub" or "chapter" or "section" ||
        provider.ToLowerInvariant() is "local" or "local-upload" or "document" or "upload" or "pdf" or "epub" ||
        provenance.Equals("personal-upload", StringComparison.OrdinalIgnoreCase);

    public static bool IsEligible(ContentItem item) => item.DocumentId == null && item.SectionId == null &&
        !IsDocumentMetadata(item.Kind, item.Provider, item.TextProvenance) && SafeUrl(item.CanonicalUrl) != null;

    private static bool IsCredentialKey(string key)
    {
        if (CredentialKeys.Contains(key)) return true;
        var normalized = key.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return normalized.EndsWith("token", StringComparison.Ordinal) || normalized.EndsWith("apikey", StringComparison.Ordinal) ||
            normalized.EndsWith("password", StringComparison.Ordinal) || normalized.EndsWith("secret", StringComparison.Ordinal) ||
            normalized.EndsWith("signature", StringComparison.Ordinal) || normalized.StartsWith("xamz", StringComparison.Ordinal) ||
            normalized.StartsWith("xgoog", StringComparison.Ordinal);
    }

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
        return !query.Contains('=') || !QueryHelpers.ParseQuery(query).Keys.Any(IsCredentialKey);
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
            if (query.Keys.Any(IsCredentialKey) || query.Values.SelectMany(values => values).Any(parameter => !SafeParameterValue(parameter ?? "", depth))) return null;
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
        IsEligible(item) && !(await DocumentDerivedIdsAsync(db, ct)).Contains(item.Id);
}

public static class PublicCatalog
{
    // Projection deliberately omits full text, metadata JSON, source IDs, user concepts,
    // and personal state. Publication and document eligibility precede every public use.
    public static async Task<List<ContentItem>> EligibleRecordsAsync(AppDbContext db, CancellationToken ct)
    {
        var blocked = await PublicCatalogPolicy.DocumentDerivedIdsAsync(db, ct);
        var records = await db.Items.AsNoTracking().Where(i => i.IsPublic && i.DocumentId == null && i.SectionId == null)
            .Select(i => new ContentItem
            {
                Id = i.Id,
                IsPublic = i.IsPublic,
                Title = i.Title,
                CanonicalUrl = i.CanonicalUrl,
                Author = i.Author,
                Excerpt = i.Excerpt,
                Kind = i.Kind,
                Provider = i.Provider,
                TextProvenance = i.TextProvenance,
                PublishedAt = i.PublishedAt,
                IngestedAt = i.IngestedAt,
                UpdatedAt = i.UpdatedAt,
                TopicIds = i.TopicIds,
                ImageUrl = i.ImageUrl,
                EmbedUrl = i.EmbedUrl
            }).ToListAsync(ct);
        return records.Where(i => !blocked.Contains(i.Id) && PublicCatalogPolicy.IsEligible(i)).ToList();
    }

    public static async Task<ContentItem?> ResolveAsync(AppDbContext db, Guid id, List<ContentItem> eligible, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        while (seen.Add(id))
        {
            var current = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => i.Id == id)
                .Select(i => new ContentItem
                {
                    Id = i.Id,
                    IsPublic = i.IsPublic,
                    MergedIntoId = i.MergedIntoId,
                    DocumentId = i.DocumentId,
                    SectionId = i.SectionId,
                    Kind = i.Kind,
                    Provider = i.Provider,
                    TextProvenance = i.TextProvenance,
                    CanonicalUrl = i.CanonicalUrl
                }).FirstOrDefaultAsync(ct);
            if (current == null || !current.IsPublic || !PublicCatalogPolicy.IsEligible(current)) return null;
            if (current.MergedIntoId is not Guid next) return eligible.FirstOrDefault(i => i.Id == id);
            id = next;
        }
        return null;
    }

    public static async Task<List<PublicItemDto>> BuildAsync(AppDbContext db, IEnumerable<ContentItem> records, CancellationToken ct)
    {
        var list = records.ToList();
        var ids = list.Select(i => i.Id).ToList();
        var topics = await db.Topics.AsNoTracking().Select(t => new PublicTopicDto(t.Id, t.Name)).ToListAsync(ct);
        // Subscription URLs and private subscription IDs never enter this contract.
        var sources = await db.ItemSources.AsNoTracking().Where(s => ids.Contains(s.ItemId))
            .Select(s => new { s.ItemId, s.Name }).ToListAsync(ct);
        var directSources = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id))
            .Join(db.Sources.AsNoTracking(), i => i.SourceId, s => (Guid?)s.Id,
                (i, s) => new { ItemId = i.Id, s.Name }).ToListAsync(ct);
        var names = sources.Concat(directSources).ToLookup(s => s.ItemId, s => ProviderAdapters.Plain(s.Name, 200));
        return list.Select(i => new PublicItemDto(i.Id, ProviderAdapters.Plain(i.Title, 400),
            PublicCatalogPolicy.SafeUrl(i.CanonicalUrl)!, i.Author == null ? null : ProviderAdapters.Plain(i.Author, 200),
            i.PublishedAt, i.IngestedAt, ProviderAdapters.Plain(i.Kind, 40), ProviderAdapters.Plain(i.Provider, 40),
            names[i.Id].Any() ? string.Join(", ", names[i.Id].Distinct().Order(StringComparer.Ordinal)).Truncate(600) : ProviderAdapters.Plain(i.Provider, 40),
            i.Excerpt == null ? null : ProviderAdapters.Plain(i.Excerpt, 600),
            topics.Where(t => i.TopicIds.Contains(t.Id)).OrderBy(t => t.Name).ToList(),
            PublicCatalogPolicy.SafeEmbed(i.EmbedUrl), PublicCatalogPolicy.SafeUrl(i.ImageUrl))).ToList();
    }

    private static string Truncate(this string value, int length) => value.Length <= length ? value : value[..length];
}
