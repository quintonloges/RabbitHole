using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace RabbitHole.Ingestion;

public sealed record FetchBatch(IReadOnlyList<NormalizedItem> Items, string? ETag = null, string? LastModified = null,
    bool NotModified = false, string? DiscoveredFeedUrl = null);

public sealed class IngestionService(AppDbContext db, IRemoteFetcher fetcher, IConfiguration config,
    ILogger<IngestionService> logger)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SourceLocks = new();
    private static readonly SemaphoreSlim UpsertLock = new(1, 1);
    private static readonly SemaphoreSlim AcademicRateLock = new(1, 1);
    private static readonly Dictionary<string, DateTimeOffset> LastAcademicRequest = new();
    private string? OpenAlexKey => config["OpenAlex:ApiKey"] ?? config["OPENALEX_API_KEY"];

    private static async Task PaceAcademicAsync(string provider, CancellationToken ct)
    {
        await AcademicRateLock.WaitAsync(ct);
        try
        {
            var gap = provider == "arxiv" ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(1);
            if (LastAcademicRequest.TryGetValue(provider, out var last))
            {
                var delay = last + gap - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            }
            LastAcademicRequest[provider] = DateTimeOffset.UtcNow;
        }
        finally { AcademicRateLock.Release(); }
    }
    public static readonly object[] Presets =
    [
        new { name = "OpenAI News", kind = "rss", url = "https://openai.com/news/rss.xml" },
        new { name = "arXiv cs.AI", kind = "rss", url = "https://export.arxiv.org/rss/cs.AI" },
        new { name = "Quanta Magazine", kind = "rss", url = "https://www.quantamagazine.org/feed/" }
    ];

    public async Task<FetchBatch> FetchSourceAsync(Source source, CancellationToken ct, bool conditional = true)
    {
        switch (source.Kind.ToLowerInvariant())
        {
            case "rss":
                {
                    var response = await fetcher.FetchAsync(new RemoteRequest(SafeRemoteFetcher.ValidateUrl(source.Url),
                        [.. ProviderAdapters.FeedTypes, .. ProviderAdapters.HtmlTypes], 1_500_000,
                        conditional ? source.ETag : null, conditional ? source.LastModified : null), ct);
                    if (response.StatusCode == 304) return new FetchBatch([], response.ETag, response.LastModified, true);
                    if (ProviderAdapters.HtmlTypes.Contains(response.ContentType))
                    {
                        var feeds = ProviderAdapters.DiscoverFeeds(response.Text, response.FinalUrl);
                        if (feeds.Count == 0) throw new FormatException("No RSS or Atom feed was discovered at this URL.");
                        var feed = await fetcher.FetchAsync(new RemoteRequest(feeds[0], ProviderAdapters.FeedTypes, 1_500_000), ct);
                        return new FetchBatch(ProviderAdapters.ParseFeed(feed.Body), feed.ETag, feed.LastModified,
                            DiscoveredFeedUrl: feed.FinalUrl.ToString());
                    }
                    return new FetchBatch(ProviderAdapters.ParseFeed(response.Body), response.ETag, response.LastModified);
                }
            case "openalex":
                {
                    var query = source.Query.Trim();
                    if (query.Length is < 2 or > 200) throw new ArgumentException("OpenAlex source needs a 2–200 character query.");
                    await PaceAcademicAsync("openalex", ct);
                    var response = await fetcher.FetchAsync(new RemoteRequest(ProviderAdapters.OpenAlexSearchUrl(query,
                        source.ResultLimit, OpenAlexKey), ProviderAdapters.JsonTypes), ct);
                    return new FetchBatch(ProviderAdapters.ParseOpenAlex(response.Text));
                }
            case "arxiv":
                {
                    var query = source.Query.Trim();
                    if (query.Length is < 2 or > 200) throw new ArgumentException("arXiv source needs a 2–200 character query.");
                    await PaceAcademicAsync("arxiv", ct);
                    var response = await fetcher.FetchAsync(new RemoteRequest(ProviderAdapters.ArxivSearchUrl(query,
                        source.ResultLimit), ProviderAdapters.FeedTypes), ct);
                    return new FetchBatch(ProviderAdapters.ParseArxiv(response.Body));
                }
            case "youtube":
                {
                    var id = ProviderAdapters.YoutubeChannelId(source.Url);
                    if (id is null) throw new ArgumentException("Use a YouTube /channel/UC… URL for channel feeds.");
                    var response = await fetcher.FetchAsync(new RemoteRequest(new Uri("https://www.youtube.com/feeds/videos.xml?channel_id=" + id),
                        ProviderAdapters.FeedTypes), ct);
                    return new FetchBatch(ProviderAdapters.ParseFeed(response.Body, "youtube"), response.ETag, response.LastModified);
                }
            default: throw new ArgumentException("Supported source kinds are rss, openalex, arxiv, and youtube.");
        }
    }

    public async Task<IngestionJob> RefreshAsync(Source source, CancellationToken ct)
    {
        var sourceLock = SourceLocks.GetOrAdd(source.Id, _ => new SemaphoreSlim(1, 1));
        await sourceLock.WaitAsync(ct);
        try
        {
            var job = new IngestionJob { SourceId = source.Id };
            db.Jobs.Add(job);
            source.LastAttemptAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            try
            {
                var batch = await FetchSourceAsync(source, ct);
                var imported = 0;
                foreach (var normalized in batch.Items.Take(Math.Clamp(source.ResultLimit, 1, 100)))
                {
                    var (_, created) = await UpsertAsync(normalized, source.Id, source.TopicIds, ct);
                    if (created) imported++;
                }
                if (batch.DiscoveredFeedUrl is not null) source.Url = batch.DiscoveredFeedUrl;
                source.ETag = batch.ETag ?? source.ETag;
                source.LastModified = batch.LastModified ?? source.LastModified;
                source.LastSuccessAt = DateTimeOffset.UtcNow;
                source.LastImportedCount = imported;
                source.LastError = null;
                source.FailureCount = 0;
                source.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(source.IntervalMinutes, 15, 10080));
                job.Imported = imported;
                job.Status = "completed";
            }
            catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("Source {SourceId} refresh failed: {Type}", source.Id, error.GetType().Name);
                source.FailureCount = Math.Min(source.FailureCount + 1, 12);
                source.LastError = PublicError(error);
                source.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(1440,
                    Math.Max(source.IntervalMinutes, 15) * Math.Pow(2, Math.Min(source.FailureCount, 5))));
                job.Status = "failed";
                job.Error = source.LastError;
            }
            catch (OperationCanceledException)
            {
                source.LastError = "Refresh interrupted; it can be retried.";
                source.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(1);
                job.Status = "interrupted";
                job.Error = source.LastError;
                job.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            job.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return job;
        }
        finally { sourceLock.Release(); }
    }

    public async Task<(ContentItem Item, bool Created)> UpsertAsync(NormalizedItem item, Guid? sourceId,
        IReadOnlyList<Guid> topics, CancellationToken ct)
    {
        await UpsertLock.WaitAsync(ct);
        try
        {
            if (!ProviderAdapters.TryPublicLink(item.CanonicalUrl, out var canonical))
                throw new FormatException("Imported item has no valid public URL.");
            var existing = await db.Items.FirstOrDefaultAsync(x =>
                x.CanonicalUrl == canonical ||
                (item.ProviderId != null && x.Provider == item.Provider && x.ProviderId == item.ProviderId) ||
                (item.Doi != null && x.Doi == item.Doi) ||
                (item.ArxivId != null && x.ArxivId == item.ArxivId), ct);
            var created = existing is null;
            if (existing is null)
            {
                existing = new ContentItem
                {
                    CanonicalUrl = canonical,
                    Provider = item.Provider,
                    ProviderId = item.ProviderId,
                    Doi = item.Doi,
                    ArxivId = item.ArxivId,
                    SourceId = sourceId,
                    TopicIds = topics.Distinct().ToList(),
                    ClassificationOrigin = sourceId is null ? "manual" : "source"
                };
                db.Items.Add(existing);
            }
            if (created || item.Warning is null || existing.Title.Length == 0)
                existing.Title = ProviderAdapters.Plain(item.Title, 400);
            if (created || item.Kind != "article") existing.Kind = item.Kind;
            existing.Author = item.Author ?? existing.Author;
            existing.Excerpt = item.Excerpt ?? existing.Excerpt;
            existing.Body = item.Body ?? existing.Body;
            if (string.IsNullOrWhiteSpace(existing.Body)) existing.Body = null;
            existing.TextProvenance = existing.Body is null ? "metadata" : item.Body is not null
                ? item.TextProvenance : existing.TextProvenance;
            existing.ImageUrl = item.ImageUrl ?? existing.ImageUrl;
            if (item.EmbedUrl is not null && ProviderAdapters.IsApprovedEmbed(item.EmbedUrl))
                existing.EmbedUrl = item.EmbedUrl;
            existing.MetadataJson = MergeMetadata(existing.MetadataJson, item.MetadataJson,
                existing.Provider, existing.ProviderId, item.Provider, item.ProviderId);
            existing.Doi ??= item.Doi;
            existing.ArxivId ??= item.ArxivId;
            existing.PublishedAt ??= item.PublishedAt;
            if (created || item.Warning is null) existing.ImportWarning = item.Warning;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            if (sourceId is not null && existing.SourceId is null) existing.SourceId = sourceId;
            if (topics.Count > 0) existing.TopicIds = existing.TopicIds.Union(topics).ToList();
            await db.SaveChangesAsync(ct);
            return (existing, created);
        }
        finally { UpsertLock.Release(); }
    }

    public async Task<ContentItem> ImportUrlAsync(string url, string? title, CancellationToken ct)
    {
        var uri = SafeRemoteFetcher.ValidateUrl(url);
        var canonical = new UriBuilder(uri) { Fragment = "" }.Uri.ToString();
        var item = new NormalizedItem
        {
            Provider = "manual",
            CanonicalUrl = canonical,
            Title = ProviderAdapters.Plain(title ?? uri.Host + uri.AbsolutePath, 400),
            Warning = "Link only: metadata was unavailable."
        };
        var videoId = ProviderAdapters.YoutubeVideoId(canonical);
        if (videoId is not null)
        {
            item.Kind = "video"; item.Provider = "youtube"; item.ProviderId = videoId;
            item.CanonicalUrl = "https://www.youtube.com/watch?v=" + videoId;
            item.EmbedUrl = "https://www.youtube-nocookie.com/embed/" + videoId;
        }
        try
        {
            if (videoId is not null)
            {
                var endpoint = new Uri("https://www.youtube.com/oembed?url=" +
                    Uri.EscapeDataString(item.CanonicalUrl) + "&format=json");
                var response = await fetcher.FetchAsync(new RemoteRequest(endpoint, ProviderAdapters.JsonTypes, 250_000), ct);
                item = ProviderAdapters.ParseYoutubeOEmbed(response.Text, videoId);
            }
            else if (ProviderAdapters.IsSoundCloudHost(uri.Host))
            {
                var endpoint = new Uri("https://soundcloud.com/oembed?format=json&url=" + Uri.EscapeDataString(canonical));
                var response = await fetcher.FetchAsync(new RemoteRequest(endpoint, ProviderAdapters.JsonTypes, 250_000), ct);
                item = ProviderAdapters.ParseSoundCloud(response.Text, canonical);
            }
            else if (await RobotsAllowedAsync(uri, ct))
            {
                var response = await fetcher.FetchAsync(new RemoteRequest(uri, ProviderAdapters.HtmlTypes, 500_000), ct);
                var metadata = ProviderAdapters.HtmlMetadata(response.Text);
                if (!string.IsNullOrWhiteSpace(metadata.Title)) item.Title = metadata.Title;
                item.Excerpt = metadata.Description;
                item.Warning = null;
            }
            else item.Warning = "Link only: the site's robots rules do not permit metadata retrieval.";
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            item.Warning = "Link only: " + PublicError(error);
        }
        if (!string.IsNullOrWhiteSpace(title)) item.Title = ProviderAdapters.Plain(title, 400);
        return (await UpsertAsync(item, null, [], ct)).Item;
    }

    private async Task<bool> RobotsAllowedAsync(Uri url, CancellationToken ct)
    {
        var robots = new Uri(url.GetLeftPart(UriPartial.Authority) + "/robots.txt");
        RemoteResponse response;
        try { response = await fetcher.FetchAsync(new RemoteRequest(robots, ["text/plain"], 100_000), ct); }
        catch (RemoteFetchException e) when (e.Message.Contains("404")) { return true; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
        var applies = false;
        var rules = new List<(bool Allow, string Path)>();
        foreach (var raw in response.Text.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            {
                var agent = line[11..].Trim();
                applies = agent is "*" || agent.Equals("RabbitHole", StringComparison.OrdinalIgnoreCase);
            }
            else if (applies && (line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase) ||
                                 line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase)))
            {
                var allowed = line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase);
                var path = line[(allowed ? 6 : 9)..].Trim();
                if (path.Length > 0) rules.Add((allowed, path));
            }
        }
        var match = rules.Where(x => RobotsMatch(url.PathAndQuery, x.Path))
            .OrderByDescending(x => x.Path.Length).FirstOrDefault();
        return match.Path is null || match.Allow;
    }

    private static bool RobotsMatch(string path, string pattern)
    {
        var escaped = Regex.Escape(pattern).Replace(@"\*", ".*");
        if (escaped.EndsWith(@"\$")) escaped = escaped[..^2] + "$";
        return Regex.IsMatch(path, "^" + escaped, RegexOptions.CultureInvariant);
    }

    public async Task<IReadOnlyList<NormalizedItem>> SearchResearchAsync(string provider, string query, CancellationToken ct)
    {
        if (query.Trim().Length is < 2 or > 200) throw new ArgumentException("Query must be 2–200 characters.");
        if (provider == "openalex")
        {
            await PaceAcademicAsync("openalex", ct);
            var response = await fetcher.FetchAsync(new RemoteRequest(ProviderAdapters.OpenAlexSearchUrl(query, 15,
                OpenAlexKey), ProviderAdapters.JsonTypes), ct);
            return ProviderAdapters.ParseOpenAlex(response.Text);
        }
        if (provider == "arxiv")
        {
            await PaceAcademicAsync("arxiv", ct);
            var response = await fetcher.FetchAsync(new RemoteRequest(ProviderAdapters.ArxivSearchUrl(query, 15), ProviderAdapters.FeedTypes), ct);
            return ProviderAdapters.ParseArxiv(response.Body);
        }
        throw new ArgumentException("Provider must be openalex or arxiv.");
    }

    public async Task<ContentItem> ImportResearchAsync(string provider, string id, CancellationToken ct)
    {
        NormalizedItem? item;
        if (provider == "openalex" && Regex.IsMatch(id, @"^W\d+$"))
        {
            await PaceAcademicAsync("openalex", ct);
            var url = "https://api.openalex.org/works/" + id;
            if (!string.IsNullOrWhiteSpace(OpenAlexKey)) url += "?api_key=" + Uri.EscapeDataString(OpenAlexKey!);
            var response = await fetcher.FetchAsync(new RemoteRequest(new Uri(url), ProviderAdapters.JsonTypes), ct);
            item = ProviderAdapters.ParseOpenAlex(response.Text).SingleOrDefault(x => x.ProviderId == id);
        }
        else if (provider == "arxiv" && Regex.IsMatch(id, @"^(\d{4}\.\d{4,5}|[a-z.-]+/\d{7})$", RegexOptions.IgnoreCase))
        {
            await PaceAcademicAsync("arxiv", ct);
            var response = await fetcher.FetchAsync(new RemoteRequest(new Uri("https://export.arxiv.org/api/query?id_list=" + Uri.EscapeDataString(id)),
                ProviderAdapters.FeedTypes), ct);
            item = ProviderAdapters.ParseArxiv(response.Body).SingleOrDefault(x => x.ArxivId == id);
        }
        else throw new ArgumentException("Invalid provider or provider ID.");
        if (item is null) throw new KeyNotFoundException("Provider did not return the requested record.");
        return (await UpsertAsync(item, null, [], ct)).Item;
    }

    public static string PublicError(Exception error) => error switch
    {
        RemoteFetchException => error.Message,
        OperationCanceledException => "The remote request timed out.",
        FormatException => "The provider returned malformed metadata.",
        JsonException => "The provider returned malformed JSON.",
        XmlException => "The provider returned malformed XML.",
        _ => "The provider is temporarily unavailable."
    };

    private static string? MergeMetadata(string? previous, string? current,
        string oldProvider, string? oldId, string newProvider, string? newId)
    {
        JsonObject root;
        try { root = JsonNode.Parse(previous ?? "{}") as JsonObject ?? new JsonObject(); }
        catch (JsonException) { root = new JsonObject(); }
        try
        {
            if (JsonNode.Parse(current ?? "{}") is JsonObject incoming)
                foreach (var property in incoming)
                    if (!root.ContainsKey(property.Key)) root[property.Key] = property.Value?.DeepClone();
        }
        catch (JsonException) { }
        var existingRefs = root["providerReferences"] as JsonArray;
        var refs = existingRefs ?? new JsonArray();
        void add(string provider, string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (refs.Any(x => x?["provider"]?.GetValue<string>() == provider && x?["id"]?.GetValue<string>() == id)) return;
            refs.Add(new JsonObject { ["provider"] = provider, ["id"] = id });
        }
        add(oldProvider, oldId);
        add(newProvider, newId);
        if (existingRefs is null && refs.Count > 0) root["providerReferences"] = refs;
        return root.Count == 0 ? null : root.ToJsonString();
    }
}
