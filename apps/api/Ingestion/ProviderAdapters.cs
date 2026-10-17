using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RabbitHole.Ingestion;

public sealed class NormalizedItem
{
    public string Kind { get; set; } = "article";
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string CanonicalUrl { get; set; } = "";
    public string Provider { get; set; } = "rss";
    public string? ProviderId { get; set; }
    public string? Doi { get; set; }
    public string? ArxivId { get; set; }
    public string? Excerpt { get; set; }
    public string? Body { get; set; }
    public string TextProvenance { get; set; } = "metadata";
    public string? ImageUrl { get; set; }
    public string? EmbedUrl { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? Warning { get; set; }
}

public static class ProviderAdapters
{
    private static readonly string[] XmlTypes = ["application/rss+xml", "application/atom+xml", "application/xml", "text/xml", "text/plain"];
    public static string[] FeedTypes => XmlTypes;
    public static string[] HtmlTypes => ["text/html", "application/xhtml+xml"];
    public static string[] JsonTypes => ["application/json", "application/ld+json"];

    public static Uri OpenAlexSearchUrl(string query, int limit, string? key)
    {
        var url = $"https://api.openalex.org/works?search={Uri.EscapeDataString(query)}&per_page={Math.Clamp(limit, 1, 25)}";
        if (!string.IsNullOrWhiteSpace(key)) url += "&api_key=" + Uri.EscapeDataString(key);
        return new Uri(url);
    }
    public static Uri ArxivSearchUrl(string query, int limit) =>
        new($"https://export.arxiv.org/api/query?search_query=all:{Uri.EscapeDataString(query)}&start=0&max_results={Math.Clamp(limit, 1, 25)}&sortBy=submittedDate&sortOrder=descending");

    public static IReadOnlyList<NormalizedItem> ParseFeed(string xml, string provider = "rss")
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_000_000
        });
        return ParseFeedDocument(XDocument.Load(reader), provider);
    }

    public static IReadOnlyList<NormalizedItem> ParseFeed(byte[] xml, string provider = "rss")
    {
        using var reader = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_000_000
        });
        return ParseFeedDocument(XDocument.Load(reader), provider);
    }

    private static IReadOnlyList<NormalizedItem> ParseFeedDocument(XDocument doc, string provider)
    {
        var root = doc.Root ?? throw new FormatException("Empty feed.");
        var atom = root.Name.LocalName == "feed";
        if (!atom && root.Name.LocalName != "rss" && root.Name.LocalName != "RDF")
            throw new FormatException("Not an RSS or Atom feed.");
        var entries = atom ? root.Elements().Where(x => x.Name.LocalName == "entry")
            : root.Descendants().Where(x => x.Name.LocalName == "item");
        var items = new List<NormalizedItem>();
        foreach (var entry in entries.Take(100))
        {
            var link = atom
                ? entry.Elements().Where(x => x.Name.LocalName == "link")
                    .Where(x => (string?)x.Attribute("rel") is null or "alternate")
                    .Select(x => (string?)x.Attribute("href")).FirstOrDefault()
                : Value(entry, "link");
            if (string.IsNullOrWhiteSpace(link)) continue;
            if (!TryPublicLink(link, out var canonical)) continue;
            var rawTitle = Value(entry, "title") ?? canonical;
            var date = Value(entry, "published") ?? Value(entry, "updated") ??
                Value(entry, "pubDate") ?? Value(entry, "date");
            var description = Value(entry, "summary") ?? Value(entry, "description") ?? Value(entry, "content");
            var suppliedContent = Value(entry, "encoded") ?? (atom ? Value(entry, "content") : null);
            var bodyText = suppliedContent is null ? null : Plain(suppliedContent, 100_000);
            if (string.IsNullOrWhiteSpace(bodyText)) bodyText = null;
            var enclosure = entry.Elements().FirstOrDefault(x => x.Name.LocalName == "enclosure" &&
                ((string?)x.Attribute("type"))?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true);
            var image = entry.Descendants().FirstOrDefault(x => x.Name.LocalName is "thumbnail" or "image")?.Attribute("url")?.Value;
            var item = new NormalizedItem
            {
                Provider = provider,
                ProviderId = Value(entry, "guid") ?? Value(entry, "id"),
                CanonicalUrl = canonical,
                Title = Plain(rawTitle, 400),
                Author = Plain(Value(entry, "creator") ?? Value(entry, "author") ?? "", 200),
                Excerpt = Plain(description ?? "", 1200),
                Body = bodyText,
                TextProvenance = bodyText is null ? "metadata" : "feed-content",
                ImageUrl = TryPublicLink(image ?? "", out var safeImage) ? safeImage : null,
                PublishedAt = ParseDate(date),
                Kind = provider == "arxiv" ? "paper" : provider == "youtube" ? "video"
                    : enclosure is not null ? "audio" : "article"
            };
            if (enclosure is not null)
            {
                var enclosureUrl = (string?)enclosure.Attribute("url");
                if (TryPublicLink(enclosureUrl ?? "", out var safeEnclosure))
                    item.MetadataJson = JsonSerializer.Serialize(new
                    {
                        enclosureUrl = safeEnclosure,
                        enclosureType = (string?)enclosure.Attribute("type")
                    });
            }
            if (provider == "arxiv")
            {
                var match = Regex.Match(canonical, @"(?:abs|pdf)/([0-9]{4}\.[0-9]{4,5}|[a-z.-]+/[0-9]{7})", RegexOptions.IgnoreCase);
                if (match.Success) item.ArxivId = match.Groups[1].Value;
                item.ProviderId = item.ArxivId;
                item.Doi = NormalizeDoi(Value(entry, "doi"));
                item.MetadataJson = JsonSerializer.Serialize(new { arxivId = item.ArxivId, doi = item.Doi });
            }
            if (provider == "youtube")
            {
                var id = YoutubeVideoId(canonical);
                if (id is null) continue;
                item.ProviderId = id;
                item.EmbedUrl = "https://www.youtube-nocookie.com/embed/" + id;
            }
            items.Add(item);
        }
        return items;
    }

    public static IReadOnlyList<NormalizedItem> ParseOpenAlex(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out var results))
            return results.EnumerateArray().Take(25).Select(ParseOpenAlexWork).Where(x => x is not null).Cast<NormalizedItem>().ToArray();
        var single = ParseOpenAlexWork(root);
        return single is null ? [] : [single];
    }

    private static NormalizedItem? ParseOpenAlexWork(JsonElement work)
    {
        var id = String(work, "id")?.Split('/').LastOrDefault();
        if (id is null || !Regex.IsMatch(id, @"^W\d+$")) return null;
        var doi = NormalizeDoi(String(work, "doi"));
        var location = work.TryGetProperty("primary_location", out var primary) && primary.ValueKind == JsonValueKind.Object
            ? String(primary, "landing_page_url") : null;
        var canonical = TryPublicLink(location ?? "", out var link) ? link : "https://openalex.org/" + id;
        string? author = null;
        if (work.TryGetProperty("authorships", out var authors) && authors.ValueKind == JsonValueKind.Array)
        {
            author = string.Join(", ", authors.EnumerateArray().Take(8)
                .Select(a => a.TryGetProperty("author", out var value) ? String(value, "display_name") : null)
                .Where(x => x is not null));
            if (authors.GetArrayLength() > 8) author += ", et al.";
        }
        var venue = work.TryGetProperty("primary_location", out var loc) && loc.ValueKind == JsonValueKind.Object &&
            loc.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object
            ? String(source, "display_name") : null;
        var openAccess = work.TryGetProperty("open_access", out var oa) && oa.ValueKind == JsonValueKind.Object &&
            oa.TryGetProperty("is_oa", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? flag.GetBoolean() : (bool?)null;
        var abstractText = ReconstructOpenAlexAbstract(work);
        string? oaUrl = null;
        if (work.TryGetProperty("best_oa_location", out var bestOa) && bestOa.ValueKind == JsonValueKind.Object)
            oaUrl = FirstPublicLink(String(bestOa, "landing_page_url"), String(bestOa, "pdf_url"));
        if (oaUrl is null && oa.ValueKind == JsonValueKind.Object)
            oaUrl = FirstPublicLink(String(oa, "oa_url"));
        var references = new List<string>();
        var referenceCount = 0;
        if (work.TryGetProperty("referenced_works", out var cited) && cited.ValueKind == JsonValueKind.Array)
        {
            foreach (var reference in cited.EnumerateArray())
            {
                if (reference.ValueKind != JsonValueKind.String) continue;
                var value = reference.GetString();
                if (value is null || !Regex.IsMatch(value, @"^https://openalex\.org/W\d+$")) continue;
                referenceCount++;
                if (references.Count < 100) references.Add(value);
            }
        }
        var citedByCount = work.TryGetProperty("cited_by_count", out var citedBy) && citedBy.ValueKind == JsonValueKind.Number &&
            citedBy.TryGetInt32(out var count) && count >= 0
            ? count : (int?)null;
        return new NormalizedItem
        {
            Kind = "paper",
            Provider = "openalex",
            ProviderId = id,
            CanonicalUrl = canonical,
            Doi = doi,
            Title = Plain(String(work, "display_name") ?? String(work, "title") ?? id, 400),
            Author = author,
            PublishedAt = ParseDate(String(work, "publication_date")),
            Excerpt = abstractText is null ? null : Plain(abstractText, 1200),
            Body = abstractText,
            TextProvenance = abstractText is null ? "metadata" : "provider-abstract",
            MetadataJson = JsonSerializer.Serialize(new
            {
                openAlexId = id,
                doi,
                venue,
                openAccess,
                openAccessStatus = oa.ValueKind == JsonValueKind.Object ? String(oa, "oa_status") : null,
                openAccessUrl = oaUrl,
                citedByCount,
                referencedWorks = references,
                referencedWorksCount = referenceCount,
                referencedWorksTruncated = referenceCount > references.Count,
                sourcePublishedDate = String(work, "publication_date")
            })
        };
    }

    private static string? FirstPublicLink(params string?[] urls)
    {
        foreach (var url in urls)
            if (TryPublicLink(url ?? "", out var safe)) return safe;
        return null;
    }

    private static string? ReconstructOpenAlexAbstract(JsonElement work)
    {
        if (!work.TryGetProperty("abstract_inverted_index", out var index) || index.ValueKind != JsonValueKind.Object)
            return null;
        const int maxWords = 5_000;
        const int maxCharacters = 30_000;
        var positions = new string?[maxWords];
        var wordCount = 0;
        var characterCount = 0;
        foreach (var entry in index.EnumerateObject())
        {
            var word = entry.Name;
            if (string.IsNullOrWhiteSpace(word) || word.Length > 200 || entry.Value.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var position in entry.Value.EnumerateArray())
            {
                if (position.ValueKind != JsonValueKind.Number || !position.TryGetInt32(out var offset) ||
                    offset < 0 || offset >= maxWords || positions[offset] is not null)
                    return null;
                positions[offset] = word;
                wordCount++;
                characterCount += word.Length + 1;
                if (wordCount > maxWords || characterCount > maxCharacters) return null;
            }
        }
        if (wordCount == 0 || positions.Take(wordCount).Any(x => x is null)) return null;
        return Plain(string.Join(' ', positions.Take(wordCount)), maxCharacters);
    }

    public static IReadOnlyList<NormalizedItem> ParseArxiv(string xml) => ParseFeed(xml, "arxiv");
    public static IReadOnlyList<NormalizedItem> ParseArxiv(byte[] xml) => ParseFeed(xml, "arxiv");

    public static NormalizedItem ParseSoundCloud(string json, string originalUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var title = String(root, "title") ?? throw new FormatException("SoundCloud response has no title.");
        var uri = String(root, "url") ?? originalUrl;
        if (!TryPublicLink(uri, out var canonical) || !IsSoundCloudHost(new Uri(canonical).Host))
            throw new FormatException("SoundCloud returned an invalid track URL.");
        return new NormalizedItem
        {
            Kind = "audio",
            Provider = "soundcloud",
            ProviderId = canonical,
            CanonicalUrl = canonical,
            Title = Plain(title, 400),
            Author = Plain(String(root, "author_name") ?? "", 200),
            ImageUrl = TryPublicLink(String(root, "thumbnail_url") ?? "", out var thumbnail) ? thumbnail : null,
            EmbedUrl = "https://w.soundcloud.com/player/?url=" + Uri.EscapeDataString(canonical)
        };
    }

    public static NormalizedItem ParseYoutubeOEmbed(string json, string videoId)
    {
        if (!Regex.IsMatch(videoId, "^[A-Za-z0-9_-]{11}$")) throw new FormatException("Invalid YouTube video ID.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var title = String(root, "title");
        if (string.IsNullOrWhiteSpace(title)) throw new FormatException("YouTube metadata has no title.");
        var thumbnail = String(root, "thumbnail_url");
        return new NormalizedItem
        {
            Kind = "video",
            Provider = "youtube",
            ProviderId = videoId,
            CanonicalUrl = "https://www.youtube.com/watch?v=" + videoId,
            Title = Plain(title, 400),
            Author = Plain(String(root, "author_name") ?? "", 200),
            ImageUrl = TryPublicLink(thumbnail ?? "", out var safeThumbnail) ? safeThumbnail : null,
            EmbedUrl = "https://www.youtube-nocookie.com/embed/" + videoId
        };
    }

    public static string? YoutubeVideoId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        string? id = null;
        if (host is "youtu.be" or "www.youtu.be") id = uri.AbsolutePath.Trim('/');
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com")
        {
            if (uri.AbsolutePath == "/watch")
                id = uri.Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("v="))?[2..];
            else if (uri.AbsolutePath.StartsWith("/shorts/") || uri.AbsolutePath.StartsWith("/embed/"))
                id = uri.AbsolutePath.Split('/').ElementAtOrDefault(2);
        }
        return id is not null && Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") ? id : null;
    }

    public static bool IsSoundCloudHost(string host) => host.Equals("soundcloud.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".soundcloud.com", StringComparison.OrdinalIgnoreCase);

    public static bool IsApprovedEmbed(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443) return false;
        if (uri.Host == "www.youtube-nocookie.com")
            return Regex.IsMatch(uri.AbsolutePath, @"^/embed/[A-Za-z0-9_-]{11}$");
        return uri.Host == "w.soundcloud.com" && uri.AbsolutePath == "/player/";
    }

    public static string? YoutubeChannelId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Host is not ("youtube.com" or "www.youtube.com")) return null;
        var match = Regex.Match(uri.AbsolutePath, @"^/channel/(UC[A-Za-z0-9_-]{22})/?$");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static IReadOnlyList<Uri> DiscoverFeeds(string html, Uri page)
    {
        var results = new List<Uri>();
        foreach (Match match in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var tag = match.Value;
            if (!Regex.IsMatch(tag, """\brel\s*=\s*['"]?alternate""", RegexOptions.IgnoreCase) ||
                !Regex.IsMatch(tag, """\btype\s*=\s*['"]?application/(rss|atom)\+xml""", RegexOptions.IgnoreCase)) continue;
            var href = Regex.Match(tag, """\bhref\s*=\s*(['"])(.*?)\1|\bhref\s*=\s*([^\s>]+)""", RegexOptions.IgnoreCase);
            var value = href.Groups[2].Success ? href.Groups[2].Value : href.Groups[3].Value;
            if (Uri.TryCreate(page, System.Net.WebUtility.HtmlDecode(value), out var candidate))
            {
                try { results.Add(SafeRemoteFetcher.ValidateUrl(candidate.ToString())); } catch (RemoteFetchException) { }
            }
        }
        return results.Distinct().Take(5).ToArray();
    }

    public static (string? Title, string? Description) HtmlMetadata(string html)
    {
        string? meta(string key) => Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(x => x.Value).Where(x => Regex.IsMatch(x, """\b(property|name)\s*=\s*['"]""" + Regex.Escape(key) + """['"]""", RegexOptions.IgnoreCase))
            .Select(x => Regex.Match(x, """\bcontent\s*=\s*(['"])(.*?)\1""", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[2].Value)
            .FirstOrDefault(x => x.Length > 0);
        var title = meta("og:title") ?? Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value;
        return (Plain(title, 400), Plain(meta("description") ?? meta("og:description") ?? "", 1200));
    }

    public static string Plain(string html, int max)
    {
        var noTags = Regex.Replace(html, "<[^>]+>", " ");
        var text = Regex.Replace(System.Net.WebUtility.HtmlDecode(noTags), @"\s+", " ").Trim();
        return text.Length > max ? text[..max] : text;
    }
    public static string? NormalizeDoi(string? doi)
    {
        if (string.IsNullOrWhiteSpace(doi)) return null;
        doi = Regex.Replace(doi.Trim(), @"^https?://(?:dx\.)?doi\.org/", "", RegexOptions.IgnoreCase);
        doi = Uri.UnescapeDataString(doi).ToLowerInvariant();
        return Regex.IsMatch(doi, @"^10\.\d{4,9}/[^\s]{1,240}$") ? doi : null;
    }
    public static bool TryPublicLink(string url, out string canonical)
    {
        canonical = "";
        try
        {
            var uri = SafeRemoteFetcher.ValidateUrl(url);
            var builder = new UriBuilder(uri) { Fragment = "" };
            canonical = builder.Uri.ToString();
            return true;
        }
        catch (RemoteFetchException) { return false; }
    }
    private static string? Value(XElement entry, string name) =>
        entry.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value;
    private static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? ParseDate(string? text) => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out var date) ? date.ToUniversalTime() : null;
}
