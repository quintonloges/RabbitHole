using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Collections.Concurrent;

namespace RabbitHole.Ingestion;

public sealed record FetchBatch(IReadOnlyList<NormalizedItem> Items, string? ETag = null, string? LastModified = null,
    bool NotModified = false, string? DiscoveredFeedUrl = null, ResearchWatchProgress? ResearchProgress = null);

public sealed record ResearchWatchProgress(string? Cursor, DateTimeOffset? FromDate,
    DateTimeOffset? WindowStartedAt, DateTimeOffset? CheckpointAt);

public sealed class IngestionService(AppDbContext db, IRemoteFetcher fetcher, IConfiguration config,
    ILogger<IngestionService> logger)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SourceLocks = new();
    private static readonly SemaphoreSlim UpsertLock = new(1, 1);
    private static readonly SemaphoreSlim AcademicRateLock = new(1, 1);
    private static readonly Dictionary<string, DateTimeOffset> LastAcademicRequest = new();
    private string? OpenAlexKey => config["OpenAlex:ApiKey"] ?? config["OPENALEX_API_KEY"];

    public static async Task<IDisposable> AcquireSourceLockAsync(Guid id, CancellationToken ct)
    {
        var gate = SourceLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new SourceLease(gate);
    }
    private sealed class SourceLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

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
    public async Task<FetchBatch> FetchSourceAsync(Source source, CancellationToken ct, bool conditional = true)
    {
        switch (source.Kind.ToLowerInvariant())
        {
            case "rss":
                return await FetchFeedAsync(source, ct, conditional);
            case "web-archive":
                return await new WebArchiveDiscovery(db, fetcher, ReadUrlMetadataAsync).FetchAsync(source, ct);
            case "openalex":
                {
                    var query = source.Query.Trim();
                    if (query.Length is < 2 or > 200) throw new ArgumentException("OpenAlex source needs a 2–200 character query.");
                    var initialSample = source.ResearchCheckpointAt is null && source.ResearchCursor is null;
                    var canPage = !string.IsNullOrWhiteSpace(OpenAlexKey);
                    var windowStartedAt = (canPage ? source.ResearchWindowStartedAt : null) ?? DateTimeOffset.UtcNow;
                    // Revisit an inclusive week to catch papers indexed after their publication date.
                    var fromDate = (canPage ? source.ResearchFromDate : null) ?? source.ResearchCheckpointAt?.AddDays(-7)
                        ?? windowStartedAt.AddYears(-2);
                    await PaceAcademicAsync("openalex", ct);
                    var response = await fetcher.FetchAsync(new RemoteRequest(ProviderAdapters.OpenAlexWatchUrl(query,
                        source.ResultLimit, OpenAlexKey, fromDate, canPage ? source.ResearchCursor : null, windowStartedAt), ProviderAdapters.JsonTypes), ct);
                    var page = ProviderAdapters.ParseOpenAlexPage(response.Text);
                    // Keyless access permits only the first cursor page: retain a bounded newest
                    // sample on the normal schedule, rather than queueing a guaranteed HTTP 401.
                    // Authenticated watches page subsequent windows over separate refreshes.
                    var hasMore = canPage && !initialSample && page.ResultCount > 0 && page.NextCursor is not null;
                    var progress = hasMore
                        ? new ResearchWatchProgress(page.NextCursor, fromDate, windowStartedAt, source.ResearchCheckpointAt)
                        : new ResearchWatchProgress(null, null, null, windowStartedAt);
                    return new FetchBatch(page.Items, ResearchProgress: progress);
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
            default: throw new ArgumentException("Supported source kinds are rss, openalex, arxiv, youtube, and web-archive.");
        }
    }

    private async Task<FetchBatch> FetchFeedAsync(Source source, CancellationToken ct, bool conditional, string? selectedItemUrl = null)
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
            return new FetchBatch(ProviderAdapters.ParseFeed(feed.Body, selectedItemUrl: selectedItemUrl), feed.ETag, feed.LastModified,
                DiscoveredFeedUrl: feed.FinalUrl.ToString());
        }
        return new FetchBatch(ProviderAdapters.ParseFeed(response.Body, selectedItemUrl: selectedItemUrl), response.ETag, response.LastModified);
    }

    public async Task<IngestionJob> RefreshAsync(Source source, CancellationToken ct)
    {
        var sourceLock = SourceLocks.GetOrAdd(source.Id, _ => new SemaphoreSlim(1, 1));
        await sourceLock.WaitAsync(ct);
        try
        {
            // The source may have waited behind another refresh, or a prior failed upsert
            // may have detached this caller's context. Always start with durable progress.
            if (db.Entry(source).State == EntityState.Detached) db.Attach(source);
            await db.Entry(source).ReloadAsync(ct);
            if (db.Entry(source).State == EntityState.Detached) throw new KeyNotFoundException("Source no longer exists.");
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
                ct.ThrowIfCancellationRequested();
                if (batch.ResearchProgress is { } progress)
                {
                    source.ResearchCursor = progress.Cursor;
                    source.ResearchFromDate = progress.FromDate;
                    source.ResearchWindowStartedAt = progress.WindowStartedAt;
                    source.ResearchCheckpointAt = progress.CheckpointAt;
                }
                if (batch.DiscoveredFeedUrl is not null) source.Url = batch.DiscoveredFeedUrl;
                source.ETag = batch.ETag ?? source.ETag;
                source.LastModified = batch.LastModified ?? source.LastModified;
                source.LastSuccessAt = DateTimeOffset.UtcNow;
                source.LastImportedCount = imported;
                source.LastError = null;
                source.FailureCount = 0;
                source.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(batch.ResearchProgress?.Cursor is not null
                    ? 1 : Math.Clamp(source.IntervalMinutes, 15, 10080));
                job.Imported = imported;
                job.Status = "completed";
                job.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning("Source {SourceId} refresh failed: {Type}", source.Id, error.GetType().Name);
                await RestoreRefreshTrackingAsync(source, job);
                source.FailureCount = Math.Min(source.FailureCount + 1, 12);
                source.LastError = PublicError(error);
                source.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(1440,
                    Math.Max(source.IntervalMinutes, 15) * Math.Pow(2, Math.Min(source.FailureCount, 5))));
                job.Status = "failed";
                job.Error = source.LastError;
                job.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                await RestoreRefreshTrackingAsync(source, job);
                source.LastError = "Refresh interrupted; it can be retried.";
                source.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(1);
                job.Status = "interrupted";
                job.Error = source.LastError;
                job.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
            return job;
        }
        finally { sourceLock.Release(); }
    }

    private async Task RestoreRefreshTrackingAsync(Source source, IngestionJob job)
    {
        // A rolled-back SaveChanges does not reset EF's tracked entities. Discard those
        // mutations before saving health; otherwise the same constraint failure repeats.
        db.ChangeTracker.Clear();
        db.Attach(source);
        await db.Entry(source).ReloadAsync(CancellationToken.None);
        db.Attach(job);
        await db.Entry(job).ReloadAsync(CancellationToken.None);
    }

    public async Task<(ContentItem Item, bool Created)> UpsertAsync(NormalizedItem item, Guid? sourceId,
        IReadOnlyList<Guid> topics, CancellationToken ct)
    {
        await UpsertLock.WaitAsync(ct);
        try
        {
            if (!ProviderAdapters.TryPublicLink(item.CanonicalUrl, out var canonical))
                throw new FormatException("Imported item has no valid public URL.");
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var doi = ProviderAdapters.NormalizeDoi(item.Doi);
            var sourceVersion = sourceId is Guid id && await db.Sources.FindAsync([id], ct) is { } source ? source.IdentityVersion : 0;
            var identities = ItemCatalog.Identities(canonical, item.Provider, item.ProviderId, doi, item.ArxivId, sourceId, sourceVersion);
            var keys = identities.Select(x => x.Key).ToList();
            var matchedIds = await db.ItemIdentities.Where(x => keys.Contains(x.Key)).Select(x => x.ItemId).ToListAsync(ct);
            var globalProvider = item.Provider is "openalex" or "arxiv" or "youtube" or "soundcloud";
            // Legacy-field fallback also supports records created outside ingestion (fixtures,
            // older imports). The identity registry becomes authoritative after backfill.
            var matches = await db.Items.Where(x => matchedIds.Contains(x.Id) ||
                x.CanonicalUrl == canonical ||
                (item.ProviderId != null && x.Provider == item.Provider && x.ProviderId == item.ProviderId &&
                    (globalProvider || (sourceVersion == 0 && sourceId != null && x.SourceId == sourceId && !db.ItemIdentities.Any(identity => identity.ItemId == x.Id)))) ||
                (doi != null && x.Doi == doi) ||
                (item.ArxivId != null && x.ArxivId == item.ArxivId)).ToListAsync(ct);
            foreach (var match in matches)
            {
                if (!await db.ItemIdentities.AnyAsync(x => x.ItemId == match.Id, ct))
                    await ItemCatalog.RegisterAsync(db, match, CatalogUpgrade.RecordIdentities(match), ct);
                await ItemCatalog.AttachSourceAsync(db, match, match.SourceId, ct);
            }
            var created = matches.Count == 0;
            ContentItem existing;
            if (created)
            {
                existing = new ContentItem
                {
                    CanonicalUrl = canonical,
                    Provider = item.Provider,
                    ProviderId = item.ProviderId,
                    Doi = doi,
                    ArxivId = item.ArxivId,
                    SourceId = sourceId,
                    ClassificationOrigin = sourceId is null ? "manual" : "source"
                };
                db.Items.Add(existing);
            }
            else existing = await ItemCatalog.MergeAsync(db, matches, ct);
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
            existing.MetadataJson = ItemCatalog.MergeMetadata(existing.MetadataJson, item.MetadataJson,
                existing.Provider, existing.ProviderId, item.Provider, item.ProviderId);
            existing.Doi ??= doi;
            existing.ArxivId ??= item.ArxivId;
            existing.PublishedAt ??= item.PublishedAt;
            if (created || item.Warning is null) existing.ImportWarning = item.Warning;
            if (created) existing.IsPublic = PublicCatalogPolicy.IsEligible(existing);
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await ItemCatalog.AttachSourceAsync(db, existing, sourceId, ct);
            await ItemCatalog.RegisterAsync(db, existing, identities, ct);
            await TopicClassification.AssignAsync(db, existing, topics, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (existing, created);
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
        finally { UpsertLock.Release(); }
    }

    public async Task<ContentItem> ImportUrlAsync(string url, string? title, CancellationToken ct, string? feedUrl = null)
    {
        var uri = SafeRemoteFetcher.ValidateUrl(url);
        var canonical = new UriBuilder(uri) { Fragment = "" }.Uri.ToString();
        if (!string.IsNullOrWhiteSpace(feedUrl))
        {
            // Explicitly selected entries from a real public feed can be imported without
            // subscribing to its entire publication. Never accept caller-supplied source text.
            var feed = await FetchFeedAsync(new Source { Url = feedUrl }, ct, conditional: false, selectedItemUrl: canonical);
            var selected = feed.Items.SingleOrDefault()
                ?? throw new RemoteFetchException("That URL was not present in the supplied feed.");
            selected.MetadataJson = ItemCatalog.MergeMetadata(selected.MetadataJson,
                JsonSerializer.Serialize(new { importedFromFeed = SafeRemoteFetcher.ValidateUrl(feedUrl).AbsoluteUri }),
                selected.Provider, selected.ProviderId, selected.Provider, selected.ProviderId);
            return (await UpsertAsync(selected, null, [], ct)).Item;
        }
        return (await UpsertAsync(await ReadUrlMetadataAsync(url, title, ct), null, [], ct)).Item;
    }

    public async Task<NormalizedItem> ReadUrlMetadataAsync(string url, string? title, CancellationToken ct)
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
                item.Author = metadata.Author;
                item.PublishedAt = metadata.PublishedAt;
                item.Warning = null;
            }
            else item.Warning = "Link only: the site's robots rules do not permit metadata retrieval.";
        }
        catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            item.Warning = "Link only: " + PublicError(error);
        }
        if (!string.IsNullOrWhiteSpace(title)) item.Title = ProviderAdapters.Plain(title, 400);
        return item;
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

}
