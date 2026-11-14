using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;

namespace RabbitHole.Ingestion;

/// <summary>Walks only publisher-advertised feed/archive links. Cursors are committed by the caller after import.</summary>
public sealed class RssArchiveBackfill(AppDbContext db, IRemoteFetcher fetcher)
{
    private const int MaxPages = 32;
    private const int MaxSitemapDepth = 2;
    private sealed class Cursor
    {
        public int TraversalVersion { get; set; }
        public string Scope { get; set; } = "";
        public string? Archive { get; set; }
        public bool Started { get; set; }
        public DateTimeOffset? RevisitAt { get; set; }
        public string? LastMessage { get; set; }
        public List<Page> Pages { get; set; } = [];
        public List<string> Visited { get; set; } = [];
    }
    private sealed class Page
    {
        public string Url { get; set; } = "";
        public int Offset { get; set; }
        public int Depth { get; set; }
    }
    private sealed record Candidate(string Url, string? Title = null, NormalizedItem? Item = null);
    private sealed record Robots(bool Available, string Text);
    private static readonly Regex Anchors = new(@"<a\b(?<attrs>[^>]*)>(?<text>.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    private static readonly Regex SensitiveQuery = new(@"(?:^|[?&])(?:[^=&]*(?:token|secret|password|signature|api[_-]?key|auth|email)[^=&]*)=", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    public async Task<BackfillBatch> FetchAsync(Source source, CancellationToken ct, DateTimeOffset? asOf = null)
    {
        var origin = SafeRemoteFetcher.ValidateUrl(source.Url);
        if (SensitiveQuery.IsMatch(origin.Query))
            return new BackfillBatch([], source.BackfillStateJson, "unsupported", "A private feed cannot establish a safe public publisher archive.");
        var prefix = ScopePrefix(origin);
        var scope = origin.GetLeftPart(UriPartial.Authority) + prefix;
        Cursor state;
        try { state = JsonSerializer.Deserialize<Cursor>(source.BackfillStateJson ?? "null") ?? new Cursor(); }
        catch (JsonException) { state = new Cursor(); }
        state.Pages = (state.Pages ?? []).Take(MaxPages).Where(p => p.Url.Length <= 4096 && p.Offset >= 0 && p.Depth is >= 0 and <= MaxSitemapDepth).ToList();
        state.Visited = (state.Visited ?? []).Where(u => u.Length <= 4096).TakeLast(128).ToList();
        var result = new List<NormalizedItem>();
        var limit = Math.Clamp(source.BackfillLimit, 1, 10);
        var now = asOf ?? DateTimeOffset.UtcNow;
        var robotsCache = new Dictionary<string, Robots>();
        string? note = null;
        var requests = 0;
        var metadataRequests = 0;

        bool Scoped(Uri uri, bool index = false) => SameSite(origin, uri) && !SensitiveQuery.IsMatch(uri.Query) &&
            (index || uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        Uri? Link(Uri page, string? raw, bool index = false)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 4096 || !Uri.TryCreate(page, WebUtility.HtmlDecode(raw), out var uri)) return null;
            try { uri = SafeRemoteFetcher.ValidateUrl(uri.AbsoluteUri); }
            catch (RemoteFetchException) { return null; }
            return Scoped(uri, index) ? new UriBuilder(uri) { Fragment = "" }.Uri : null;
        }
        async Task<Robots> GetRobots(Uri uri)
        {
            var authority = uri.GetLeftPart(UriPartial.Authority);
            if (robotsCache.TryGetValue(authority, out var cached)) return cached;
            try
            {
                var response = await fetcher.FetchAsync(new RemoteRequest(new Uri(authority + "/robots.txt"), ["text/plain"], 100_000), ct);
                cached = new Robots(SameSite(uri, response.FinalUrl) && Scoped(response.FinalUrl, true), response.Text);
            }
            // RFC 9309 section 2.3.1.3 permits an unavailable robots resource.
            // Feed-only hosts can return 406 for this path while serving their RSS normally.
            catch (RemoteFetchException e) when (UnavailableResource(e)) { cached = new Robots(true, ""); }
            robotsCache[authority] = cached ?? new Robots(false, "");
            return robotsCache[authority];
        }
        async Task<bool> Allowed(Uri uri)
        {
            var robots = await GetRobots(uri);
            if (!robots.Available) return false;
            var applies = false;
            var groupHasRules = false;
            var rules = new List<(bool Allow, string Path)>();
            foreach (var raw in robots.Text.Split('\n'))
            {
                var line = raw.Split('#')[0].Trim();
                if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
                {
                    if (groupHasRules) { applies = false; groupHasRules = false; }
                    applies |= line[11..].Trim() == "*" || line[11..].Trim().Equals("RabbitHole", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase))
                {
                    groupHasRules = true;
                    if (!applies) continue;
                    var allow = line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase);
                    var path = line[(allow ? 6 : 9)..].Trim();
                    if (path.Length > 0) rules.Add((allow, path));
                }
            }
            var match = rules.Where(r => Regex.IsMatch(uri.PathAndQuery, "^" + Regex.Escape(r.Path).Replace(@"\*", ".*").Replace(@"\$", "$"), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                .OrderByDescending(r => r.Path.Length).ThenByDescending(r => r.Allow).FirstOrDefault();
            return match.Path is null || match.Allow;
        }
        async Task<RemoteResponse?> Read(Uri uri, bool index = false)
        {
            if (!Scoped(uri, index)) { note = "An archive link or redirect left the publisher scope and was skipped."; return null; }
            if (!await Allowed(uri)) { note = "Publisher robots rules do not permit this archive page."; return null; }
            try
            {
                var response = await fetcher.FetchAsync(new RemoteRequest(uri, [.. ProviderAdapters.FeedTypes, .. ProviderAdapters.HtmlTypes], 1_500_000), ct);
                if (!SameSite(uri, response.FinalUrl) || !Scoped(response.FinalUrl, index)) { note = "An archive redirect left the publisher scope and was skipped."; return null; }
                if (!await Allowed(response.FinalUrl)) { note = "Publisher robots rules do not permit the redirected archive page."; return null; }
                return response;
            }
            catch (RemoteFetchException e) when (UnavailableResource(e)) { note = "An advertised archive page is unavailable (HTTP 404, 406, or 410)."; return null; }
        }
        void Enqueue(Uri? uri, int depth = 0, bool first = false)
        {
            // Reserve a slot for each remaining nesting level, so an index can
            // always put a child ahead of itself without dropping its own cursor.
            if (uri is null || state.Pages.Count >= PageLimit(depth) || state.Pages.Any(p => p.Url == uri.AbsoluteUri) || state.Visited.Contains(uri.AbsoluteUri)) return;
            var p = new Page { Url = uri.AbsoluteUri, Depth = depth };
            if (first) state.Pages.Insert(0, p); else state.Pages.Add(p);
        }
        async Task<bool> Add(Candidate candidate, Uri discoveredAt)
        {
            // A feed can be hosted separately from its articles. Supplied metadata needs no
            // article request; only discovered archive pages must remain within publisher scope.
            Uri? uri;
            if (candidate.Item is not null)
            {
                if (candidate.Url.Length > 4096) return true;
                try { uri = SafeRemoteFetcher.ValidateUrl(candidate.Url); }
                catch (RemoteFetchException) { return true; }
                if (SensitiveQuery.IsMatch(uri.Query)) return true;
                uri = new UriBuilder(uri) { Fragment = "" }.Uri;
            }
            else uri = Link(discoveredAt, candidate.Url);
            if (uri is null || (candidate.Item is null && !ArticlePath(uri)) || result.Any(i => i.CanonicalUrl == uri.AbsoluteUri) ||
                await SourceBackfill.IsKnownUrlAsync(db, source, uri.AbsoluteUri, ct)) return true;
            var item = candidate.Item;
            if (item is null)
            {
                if (metadataRequests >= limit * 2) return false;
                metadataRequests++;
                if (!await Allowed(uri)) { note = "Some article metadata is disallowed by publisher robots rules."; return true; }
                RemoteResponse? response;
                try { response = await fetcher.FetchAsync(new RemoteRequest(uri, ProviderAdapters.HtmlTypes, 1_500_000), ct); }
                catch (RemoteFetchException e) when (UnusableArticle(e))
                {
                    note = e.Message.Contains("size limit", StringComparison.OrdinalIgnoreCase)
                        ? "An archive article exceeded the 1.5 MB metadata limit and was skipped."
                        : "Some archive articles were unavailable or permission-limited (HTTP 401, 403, 404, or 410) and were skipped.";
                    return true;
                }
                if (!SameSite(uri, response.FinalUrl) || !Scoped(response.FinalUrl) || !await Allowed(response.FinalUrl)) { note = "An article redirect left the permitted publisher scope."; return true; }
                var metadata = ProviderAdapters.HtmlMetadata(response.Text);
                var title = metadata.Title ?? candidate.Title;
                if (string.IsNullOrWhiteSpace(title)) return true; // A sitemap URL alone is not enough evidence of an article.
                item = new NormalizedItem
                {
                    CanonicalUrl = uri.AbsoluteUri,
                    Title = title,
                    Author = metadata.Author,
                    Excerpt = metadata.Description,
                    PublishedAt = metadata.PublishedAt,
                    Provider = "rss"
                };
            }
            if (await SourceBackfill.IsKnownAsync(db, source, item, ct)) return true;
            item.MetadataJson = ItemCatalog.MergeMetadata(item.MetadataJson,
                JsonSerializer.Serialize(new { discoveredVia = discoveredAt.AbsoluteUri, discoveryProvider = "publisher archive" }),
                item.Provider, item.ProviderId, item.Provider, item.ProviderId);
            result.Add(item);
            return true;
        }

        // First consume the unused portion of the actual current feed, preserving its supplied source text.
        var feed = await Read(origin);
        if (feed is null) return Batch("unavailable", note ?? "The source feed is unavailable.");
        if (ProviderAdapters.HtmlTypes.Contains(feed.ContentType))
        {
            var discovered = ProviderAdapters.DiscoverFeeds(feed.Text, feed.FinalUrl).Select(u => Link(feed.FinalUrl, u.AbsoluteUri)).FirstOrDefault(u => u is not null);
            if (discovered is null) return Batch("unsupported", "No RSS or Atom feed was advertised by this page.");
            feed = await Read(discovered);
            if (feed is null) return Batch("unavailable", note);
        }
        var feedDoc = Xml(feed.Body);
        // The advertised publication homepage can locate an archive when the feed endpoint
        // lives elsewhere. A category/tag subscription must never expand to the whole site.
        var homeLink = Home(feedDoc, feed.FinalUrl);
        if (Link(feed.FinalUrl, homeLink, true) is { } homeUri)
        {
            var homePrefix = homeUri.AbsolutePath.TrimEnd('/') + "/";
            if (homePrefix.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                (prefix is "/blog/" or "/blogs/" && homePrefix != "/" && homePrefix.EndsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                prefix = homePrefix;
        }
        scope = origin.GetLeftPart(UriPartial.Authority) + prefix;
        // Older full queues did not reserve descent capacity. Rediscover their
        // advertised roots once; known-item checks preserve existing imports and state.
        if (state.Scope != scope || state.Archive != source.BackfillUrl ||
            state.TraversalVersion == 0 && state.Pages.Count > PageLimit(0))
            state = new Cursor { Scope = scope, Archive = source.BackfillUrl };
        state.TraversalVersion = 1;
        var feedItems = FeedItems(feedDoc);
        foreach (var item in feedItems.Skip(Math.Clamp(source.ResultLimit, 1, 100)))
        {
            await Add(new Candidate(item.CanonicalUrl, item.Title, item), feed.FinalUrl);
            if (result.Count >= limit) return Batch();
        }

        if (!state.Started && state.RevisitAt > now)
            return Batch("limited", state.LastMessage ?? "The inspected publisher archives exposed no more unseen entries; they will be checked again later.");
        if (!state.Started)
        {
            state.Started = true;
            if (!string.IsNullOrWhiteSpace(source.BackfillUrl))
            {
                var configured = Link(origin, source.BackfillUrl, true);
                if (configured is null) return Batch("unsupported", "The configured archive must be a public URL on the same publisher site.");
                Enqueue(configured);
            }
            else
            {
                foreach (var link in ArchiveLinks(feedDoc, feed.FinalUrl)) Enqueue(Link(feed.FinalUrl, link));
                var robots = await GetRobots(origin);
                foreach (var line in robots.Text.Split('\n').Where(l => l.TrimStart().StartsWith("Sitemap:", StringComparison.OrdinalIgnoreCase)).Take(4))
                    Enqueue(Link(origin, line.Trim()[8..].Trim(), true));
                if (state.Pages.Count == 0) Enqueue(new Uri(origin.GetLeftPart(UriPartial.Authority) + "/sitemap.xml"));
                var home = Home(feedDoc, feed.FinalUrl);
                Enqueue(Link(origin, home) ?? new Uri(scope));
            }
        }
        while (state.Pages.Count > 0 && requests < 3 && result.Count < limit && metadataRequests < limit * 2)
        {
            ct.ThrowIfCancellationRequested();
            var page = state.Pages[0];
            var uri = Link(origin, page.Url, true);
            requests++;
            var response = uri is null ? null : await Read(uri, true);
            if (response is null) { Finish(page); continue; }
            IReadOnlyList<Candidate> candidates;
            var next = new List<Uri>();
            if (ProviderAdapters.HtmlTypes.Contains(response.ContentType))
            {
                candidates = HtmlCandidates(response.Text, response.FinalUrl).Where(c => Link(response.FinalUrl, c.Url) is not null).ToArray();
                next.AddRange(OlderLinks(response.Text, response.FinalUrl).Select(l => Link(response.FinalUrl, l)).OfType<Uri>());
            }
            else
            {
                var doc = Xml(response.Body);
                var name = doc.Root?.Name.LocalName;
                if (name == "sitemapindex")
                {
                    var children = doc.Root!.Elements().Where(e => e.Name.LocalName == "sitemap").Select(e => e.Elements().FirstOrDefault(x => x.Name.LocalName == "loc")?.Value).ToArray();
                    if (page.Depth >= MaxSitemapDepth) { note = "Nested sitemap depth limit reached; other advertised pages remain eligible."; Finish(page); continue; }
                    // Keep the index cursor until every child was queued; no truncation of a large index.
                    var slots = Math.Max(0, PageLimit(page.Depth + 1) - state.Pages.Count);
                    if (slots == 0 && page.Offset < children.Length)
                    {
                        state.Pages.Remove(page);
                        state.Pages.Add(page);
                        continue;
                    }
                    var childrenToAdd = children.Skip(page.Offset).Take(slots).ToArray();
                    page.Offset += childrenToAdd.Length;
                    if (page.Offset >= children.Length) Finish(page);
                    foreach (var child in childrenToAdd.Reverse()) Enqueue(Link(response.FinalUrl, child, true), page.Depth + 1, first: true);
                    continue;
                }
                if (name == "urlset")
                    candidates = doc.Root!.Elements().Where(e => e.Name.LocalName == "url")
                        .Select(e => e.Elements().FirstOrDefault(x => x.Name.LocalName == "loc")?.Value).OfType<string>().Select(u => new Candidate(u)).ToArray();
                else if (name is "feed" or "rss" or "RDF")
                {
                    candidates = FeedItems(doc).Select(i => new Candidate(i.CanonicalUrl, i.Title, i)).ToArray();
                    next.AddRange(ArchiveLinks(doc, response.FinalUrl).Select(l => Link(response.FinalUrl, l)).OfType<Uri>());
                }
                else { note = "The advertised archive is not an RSS, Atom, sitemap, or supported HTML archive."; Finish(page); continue; }
            }
            while (page.Offset < candidates.Count && result.Count < limit)
            {
                if (!await Add(candidates[page.Offset], response.FinalUrl)) break;
                page.Offset++;
            }
            if (page.Offset >= candidates.Count)
            {
                Finish(page);
                foreach (var link in next) Enqueue(link);
            }
            else break;
        }
        return Batch(result.Count > 0 || state.Pages.Count > 0 ? "ready" : "limited",
            note ?? (state.Pages.Count == 0 ? "No more unseen entries were exposed by the inspected publisher pages. Recent refresh continues; archive discovery will revisit later." : null));

        void Finish(Page page)
        {
            state.Pages.Remove(page);
            state.Visited.Add(page.Url);
            state.Visited = state.Visited.TakeLast(128).ToList();
        }
        BackfillBatch Batch(string status = "ready", string? message = null)
        {
            if (state.Started && state.Pages.Count == 0)
            {
                state.Started = false;
                state.Visited.Clear();
                state.RevisitAt = now.AddDays(7);
                state.LastMessage = message;
            }
            return new BackfillBatch(result, JsonSerializer.Serialize(state), status, message);
        }
    }

    private static bool UnavailableResource(RemoteFetchException e) => Regex.IsMatch(e.Message, @"\bHTTP (?:404|406|410)\b");
    private static bool UnusableArticle(RemoteFetchException e) =>
        Regex.IsMatch(e.Message, @"\bHTTP (?:401|403|404|410)\b", RegexOptions.IgnoreCase) ||
        e.Message.Contains("size limit", StringComparison.OrdinalIgnoreCase);
    private static int PageLimit(int depth) => MaxPages - MaxSitemapDepth + depth;
    private static bool SameSite(Uri left, Uri right) =>
        left.IdnHost.TrimStartWww().Equals(right.IdnHost.TrimStartWww(), StringComparison.OrdinalIgnoreCase) &&
        (left.Scheme == right.Scheme && left.Port == right.Port ||
            left.Scheme == "http" && left.Port == 80 && right.Scheme == "https" && right.Port == 443);
    private static string ScopePrefix(Uri uri)
    {
        var path = uri.AbsolutePath;
        var feedSuffix = Regex.Match(path, @"/(?:feeds/(?:posts/(?:default|full)|all\.atom\.xml)|atom/everything|(?:feed|rss|atom)(?:\.xml)?)/?$", RegexOptions.IgnoreCase);
        if (feedSuffix.Success) return path[..(feedSuffix.Index + 1)];
        if (path.EndsWith('/')) return path;
        var last = path[(path.LastIndexOf('/') + 1)..];
        if (last.Contains('.') || Regex.IsMatch(last, @"^(?:feed|rss|atom)$", RegexOptions.IgnoreCase)) return path[..(path.LastIndexOf('/') + 1)];
        return path + "/";
    }
    private static bool ArticlePath(Uri uri)
    {
        if (uri.Query.Length > 0 && Regex.IsMatch(uri.Query, @"(?:^|[?&])(?:s|q|search|page)=", RegexOptions.IgnoreCase)) return false;
        var path = uri.AbsolutePath.Trim('/');
        return path.Length > 0 && path != "podcast" && !Regex.IsMatch(path, @"(?:^|/)(?:about|archive|archives|tags?|category|categories|login|signin|signup|subscribe|search|contact|privacy|terms|page|feed|rss)(?:/|$)|\.(?:xml|rss|atom|json|jpg|png|pdf|zip)$", RegexOptions.IgnoreCase);
    }
    private static XDocument Xml(byte[] bytes)
    {
        using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        return XDocument.Load(reader);
    }
    private static IReadOnlyList<NormalizedItem> FeedItems(XDocument doc)
    {
        var root = doc.Root ?? throw new FormatException("The archive feed is empty.");
        var atom = root.Name.LocalName == "feed";
        var entries = (atom ? root.Elements().Where(e => e.Name.LocalName == "entry") : root.Descendants().Where(e => e.Name.LocalName == "item")).ToArray();
        var items = new List<NormalizedItem>();
        var template = new XDocument(doc);
        if (atom) template.Root!.Elements().Where(e => e.Name.LocalName == "entry").Remove();
        else template.Root!.Descendants().Where(e => e.Name.LocalName == "item").Remove();
        // ProviderAdapters caps ordinary refreshes at 100. Chunk this already size-bounded document without losing supplied body text.
        foreach (var chunk in entries.Chunk(100))
        {
            var copy = new XDocument(template);
            var copyRoot = copy.Root!;
            if (atom) copyRoot.Add(chunk.Select(e => new XElement(e)));
            else
            {
                (copyRoot.Elements().FirstOrDefault(e => e.Name.LocalName == "channel") ?? copyRoot).Add(chunk.Select(e => new XElement(e)));
            }
            items.AddRange(ProviderAdapters.ParseFeed(copy.ToString(SaveOptions.DisableFormatting)));
        }
        if (root.Name.LocalName is not ("feed" or "rss" or "RDF")) throw new FormatException("Not an RSS or Atom feed.");
        return items;
    }
    private static IEnumerable<string> ArchiveLinks(XDocument doc, Uri page) => doc.Root!.DescendantsAndSelf().Where(e => e.Name.LocalName == "link" &&
        (string?)e.Attribute("rel") is "next" or "prev-archive").Select(e => (string?)e.Attribute("href")).OfType<string>();
    private static string? Home(XDocument doc, Uri page)
    {
        var channel = doc.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == "channel") ?? doc.Root;
        return channel.Elements().Where(e => e.Name.LocalName == "link" && ((string?)e.Attribute("rel") is null or "alternate"))
            .Select(e => (string?)e.Attribute("href") ?? e.Value).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
    }
    private static string? Attr(string attrs, string name)
    {
        var match = Regex.Match(attrs, @"\b" + name + "\\s*=\\s*(?:\"(?<v>[^\"]*)\"|'(?<v>[^']*)'|(?<v>[^\\s>]+))", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return match.Success ? WebUtility.HtmlDecode(match.Groups["v"].Value) : null;
    }
    private static IEnumerable<Candidate> HtmlCandidates(string html, Uri page)
    {
        // Heading links and article elements give stronger article evidence than arbitrary navigation anchors.
        var blocks = Regex.Matches(html, @"<h[1-6]\b[^>]*>.*?</h[1-6]\s*>|<article\b[^>]*>.*?</article\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        var links = blocks.Cast<Match>().SelectMany(b => Anchors.Matches(b.Value).Cast<Match>()).Concat(
            Anchors.Matches(html).Cast<Match>().Where(a =>
                Regex.IsMatch(Attr(a.Groups["attrs"].Value, "class") ?? "", @"(?:post|entry)[-_]title", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(Attr(a.Groups["attrs"].Value, "href") ?? "", @"/(?:p/[^/?#]+|\d{4}/\d{1,2}/[^/?#]+)")));
        return links
            .Select(a => new Candidate(Attr(a.Groups["attrs"].Value, "href") ?? "", ProviderAdapters.Plain(a.Groups["text"].Value, 400)))
            .Where(c => c.Url.Length > 0 && c.Title?.Length > 0).DistinctBy(c => c.Url).ToArray();
    }
    private static IEnumerable<string> OlderLinks(string html, Uri page) => Anchors.Matches(html).Cast<Match>().Where(a =>
        (Attr(a.Groups["attrs"].Value, "rel") ?? "").Split(' ').Contains("next", StringComparer.OrdinalIgnoreCase) ||
        Regex.IsMatch(ProviderAdapters.Plain(a.Groups["text"].Value, 200), @"\b(?:older(?:\s+(?:posts|entries|articles))?|next\s+(?:page|posts|entries))\b", RegexOptions.IgnoreCase))
        .Select(a => Attr(a.Groups["attrs"].Value, "href")).Concat(
            Regex.Matches(html, @"<link\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).Cast<Match>()
                .Where(l => (Attr(l.Groups["attrs"].Value, "rel") ?? "").Split(' ').Contains("next", StringComparer.OrdinalIgnoreCase))
                .Select(l => Attr(l.Groups["attrs"].Value, "href"))).OfType<string>().Distinct().Take(2);
}

internal static class ArchiveHostExtensions
{
    public static string TrimStartWww(this string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}
