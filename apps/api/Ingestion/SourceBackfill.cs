using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;

namespace RabbitHole.Ingestion;

public sealed record BackfillBatch(IReadOnlyList<NormalizedItem> Items, string? StateJson,
    string Status = "ready", string? Message = null);

/// <summary>Read-only archive adapters. The runner commits progress only after imports succeed.</summary>
public sealed class SourceBackfill(AppDbContext db, IRemoteFetcher fetcher, IConfiguration config, IYtDlpClient? ytDlp = null)
{
    private sealed class Cursor
    {
        public string Engine { get; set; } = "";
        public DateTimeOffset? Through { get; set; }
        public string? Token { get; set; }
        public string? Playlist { get; set; }
        public int Offset { get; set; }
    }

    public Task<BackfillBatch> FetchAsync(Source source, CancellationToken ct, DateTimeOffset? asOf = null) => source.Kind switch
    {
        "rss" => new RssArchiveBackfill(db, fetcher).FetchAsync(source, ct, asOf),
        "youtube" => YoutubeAsync(source, asOf ?? DateTimeOffset.UtcNow, ct),
        "openalex" => OpenAlexAsync(source, asOf ?? DateTimeOffset.UtcNow, ct),
        "arxiv" => ArxivAsync(source, asOf ?? DateTimeOffset.UtcNow, ct),
        "web-archive" => Task.FromResult(new BackfillBatch([], source.BackfillStateJson, "unsupported",
            "This source already discovers older links on its normal daily schedule.")),
        _ => Task.FromResult(new BackfillBatch([], source.BackfillStateJson, "unsupported",
            "This source does not expose a supported archive. Its regular refresh is unchanged."))
    };

    private static Cursor ReadCursor(Source source, DateTimeOffset now)
    {
        Cursor? state = null;
        if (!string.IsNullOrWhiteSpace(source.BackfillStateJson))
        {
            if (source.BackfillStateJson.Length > 1_000_000) throw new FormatException("Archive cursor is too large.");
            using var envelope = JsonDocument.Parse(source.BackfillStateJson, new JsonDocumentOptions { MaxDepth = 10 });
            var otherEngine = envelope.RootElement.ValueKind == JsonValueKind.Object && envelope.RootElement.TryGetProperty("Engine", out var engine) &&
                engine.ValueKind == JsonValueKind.String && engine.GetString() != source.Kind;
            if (!otherEngine)
            {
                if (source.BackfillStateJson.Length > 20_000) throw new FormatException("Archive cursor is too large.");
                state = JsonSerializer.Deserialize<Cursor>(source.BackfillStateJson, new JsonSerializerOptions { MaxDepth = 8 });
            }
        }
        if (state is null || state.Engine != source.Kind) state = new Cursor { Engine = source.Kind };
        // Snapshot history independently of the ongoing new-content watch.
        state.Through ??= now.AddDays(-30);
        if (state.Through > now || state.Through < DateTimeOffset.Parse("1800-01-01T00:00:00Z", CultureInfo.InvariantCulture) ||
            state.Offset is < 0 or > 30_000 || state.Token?.Length > 2048 || state.Playlist?.Length > 128)
            throw new FormatException("Invalid archive cursor.");
        return state;
    }

    private static BackfillBatch Batch(IEnumerable<NormalizedItem> items, Cursor cursor, bool finished, string? message = null)
    {
        if (finished) { cursor.Token = null; cursor.Offset = 0; cursor.Through = null; }
        return new(items.ToArray(), JsonSerializer.Serialize(cursor), finished ? "caught-up" : "ready", message);
    }

    private async Task<BackfillBatch> OpenAlexAsync(Source source, DateTimeOffset now, CancellationToken ct)
    {
        var key = config["OpenAlex:ApiKey"] ?? config["OPENALEX_API_KEY"];
        if (string.IsNullOrWhiteSpace(key)) return new([], source.BackfillStateJson, "needs-key",
            "Older paper pagination needs OPENALEX_API_KEY on the server. Regular research refreshes are unchanged.");
        var state = ReadCursor(source, now);
        var limit = Math.Clamp(source.BackfillLimit, 1, 10);
        await IngestionService.PaceAcademicAsync("openalex", ct);
        var url = ProviderAdapters.OpenAlexWatchUrl(source.Query, limit, key, null, state.Token, state.Through);
        RemoteResponse response;
        try { response = await fetcher.FetchAsync(new RemoteRequest(url, ProviderAdapters.JsonTypes, 1_000_000), ct); }
        catch (RemoteFetchException error) when (state.Token is not null && error.Message.Contains("HTTP 400", StringComparison.Ordinal))
        {
            state.Token = null;
            return Batch([], state, false, "The provider rejected the saved cursor. The next daily pass restarts this archive snapshot and skips known papers.");
        }
        var page = ProviderAdapters.ParseOpenAlexPage(response.Text);
        if (page.ResultCount > limit || page.NextCursor?.Length > 2048) throw new FormatException("Unexpected archive page size.");
        var items = new List<NormalizedItem>();
        foreach (var item in page.Items)
            if ((item.PublishedAt is null || item.PublishedAt <= state.Through) && !await IsKnownAsync(db, source, item, ct)) items.Add(item);
        var finished = page.ResultCount == 0 || page.NextCursor is null || page.NextCursor == state.Token;
        state.Token = page.NextCursor;
        return Batch(items, state, finished, finished
            ? "Reached the end of this provider snapshot. Future daily passes check for additional older records."
            : "Continuing through older matching papers independently of recent updates.");
    }

    private async Task<BackfillBatch> ArxivAsync(Source source, DateTimeOffset now, CancellationToken ct)
    {
        var state = ReadCursor(source, now);
        var limit = Math.Clamp(source.BackfillLimit, 1, 10);
        var initial = ProviderAdapters.ArxivSearchUrl(source.Query, limit);
        var query = Uri.UnescapeDataString(initial.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("search_query="))[13..]);
        var through = state.Through!.Value.UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var expression = $"({query}) AND submittedDate:[199101010000 TO {through}]";
        var url = new Uri("https://export.arxiv.org/api/query?search_query=" + Uri.EscapeDataString(expression) +
            "&start=" + state.Offset.ToString(CultureInfo.InvariantCulture) + "&max_results=" + limit + "&sortBy=submittedDate&sortOrder=descending");
        await IngestionService.PaceAcademicAsync("arxiv", ct);
        var response = await fetcher.FetchAsync(new RemoteRequest(url, ProviderAdapters.FeedTypes, 1_000_000), ct);
        using var reader = XmlReader.Create(new StringReader(response.Text), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "feed") throw new FormatException("Invalid arXiv archive feed.");
        var rawCount = document.Root.Elements().Count(e => e.Name.LocalName == "entry");
        if (rawCount > limit) throw new FormatException("Unexpected arXiv page size.");
        if (document.Descendants().Any(e => e.Name.LocalName == "id" && e.Value.Contains("api/errors", StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("arXiv rejected the archive query.");
        var items = new List<NormalizedItem>();
        var parsed = ProviderAdapters.ParseArxiv(response.Body);
        foreach (var item in parsed)
            if ((item.PublishedAt is null || item.PublishedAt <= state.Through) && !await IsKnownAsync(db, source, item, ct)) items.Add(item);
        state.Offset += rawCount;
        var totalValue = document.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "totalResults")?.Value;
        var hasTotal = int.TryParse(totalValue, NumberStyles.None, CultureInfo.InvariantCulture, out var total);
        var finished = rawCount < limit || (hasTotal && state.Offset >= total);
        if (!finished && state.Offset >= 30_000)
        {
            // Respect arXiv's pagination ceiling; move into an earlier date window.
            var oldest = parsed.Select(i => i.PublishedAt).Min();
            if (oldest.HasValue)
            {
                // Overlap the boundary minute so papers with equal timestamps are not dropped.
                var date = oldest.Value.UtcDateTime;
                var boundary = new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0, TimeSpan.Zero).AddMinutes(1);
                if (boundary < state.Through) { state.Through = boundary; state.Offset = 0; }
                else return Batch(items, state, true, "The provider's 30,000-result limit was reached within one date window. A narrower research query can expose more history.");
            }
            else return Batch(items, state, true, "The provider's 30,000-result limit was reached without publication dates to continue. A narrower research query can expose more history.");
        }
        return Batch(items, state, finished, finished
            ? "Reached the end of this matching paper snapshot. Daily checks continue."
            : "Continuing through older matching papers independently of recent updates.");
    }

    private async Task<BackfillBatch> YoutubeAsync(Source source, DateTimeOffset now, CancellationToken ct, bool checkOnly = false)
    {
        var mode = config["YouTube:BackfillMode"] ?? config["YOUTUBE_BACKFILL_MODE"] ?? "yt-dlp";
        if (mode.Equals("yt-dlp", StringComparison.OrdinalIgnoreCase))
        {
            var adapter = new YoutubeArchiveBackfill(db, ytDlp ?? new YtDlpClient(config));
            return checkOnly ? await adapter.CheckAsync(source, ct) : await adapter.FetchAsync(source, now, ct);
        }
        if (!mode.Equals("api", StringComparison.OrdinalIgnoreCase))
            throw new YtDlpException("YouTube archive configuration is invalid. Set YOUTUBE_BACKFILL_MODE to yt-dlp or api.", "configuration");
        var key = config["YouTube:ApiKey"] ?? config["YOUTUBE_API_KEY"];
        if (string.IsNullOrWhiteSpace(key))
        {
            const string message = "Older videos need YOUTUBE_API_KEY on the server. Recent channel videos still arrive through the normal feed.";
            if (checkOnly) throw new RemoteFetchException(message);
            return new([], source.BackfillStateJson, "needs-key", message);
        }
        var channel = ProviderAdapters.YoutubeChannelId(source.Url) ?? throw new FormatException("Use a YouTube channel URL.");
        var state = ReadCursor(source, now);
        async Task<JsonDocument> Fetch(string path)
        {
            var url = new Uri("https://www.googleapis.com/youtube/v3/" + path + "&key=" + Uri.EscapeDataString(key));
            return JsonDocument.Parse((await fetcher.FetchAsync(new RemoteRequest(url, ProviderAdapters.JsonTypes, 1_000_000), ct)).Body);
        }
        if (state.Playlist is null)
        {
            using var response = await Fetch("channels?part=contentDetails&id=" + channel);
            var channels = RequiredItems(response.RootElement);
            var selected = channels.EnumerateArray().FirstOrDefault(c => Text(c, "id") == channel);
            state.Playlist = selected.ValueKind == JsonValueKind.Object && selected.TryGetProperty("contentDetails", out var details) &&
                details.TryGetProperty("relatedPlaylists", out var playlists) ? Text(playlists, "uploads") : null;
            if (state.Playlist is null || !Regex.IsMatch(state.Playlist, @"^[A-Za-z0-9_-]{10,128}$"))
                throw new FormatException("The channel did not expose an uploads playlist.");
        }
        var path = "playlistItems?part=snippet,contentDetails,status&maxResults=50&playlistId=" + Uri.EscapeDataString(state.Playlist);
        if (state.Token is not null) path += "&pageToken=" + Uri.EscapeDataString(state.Token);
        JsonDocument page;
        try { page = await Fetch(path); }
        catch (RemoteFetchException error) when (!checkOnly && state.Token is not null && error.Message.Contains("HTTP 400", StringComparison.Ordinal))
        {
            state.Token = null; state.Offset = 0;
            return Batch([], state, false, "The provider rejected the saved page token. The next daily pass restarts the archive and skips known videos.");
        }
        using (page)
        {
            var entries = RequiredItems(page.RootElement);
            var count = entries.GetArrayLength();
            if (count > 50 || state.Offset > 50) throw new FormatException("Invalid YouTube archive page.");
            var next = Text(page.RootElement, "nextPageToken");
            if (next?.Length > 2048) throw new FormatException("Invalid YouTube page token.");
            if (checkOnly) return new([], source.BackfillStateJson, "ready",
                "YouTube Data API health check passed: the uploads metadata request succeeded. No items were imported.");
            var result = new List<NormalizedItem>();
            var offset = Math.Min(state.Offset, count);
            foreach (var entry in entries.EnumerateArray().Skip(offset))
            {
                offset++;
                var item = ParseYoutubeUpload(entry, channel);
                if (item is null || item.PublishedAt > state.Through || await IsKnownAsync(db, source, item, ct)) continue;
                result.Add(item);
                if (result.Count >= Math.Clamp(source.BackfillLimit, 1, 10)) break;
            }
            state.Offset = offset;
            var finished = offset >= count && (next is null || next == state.Token);
            if (offset >= count) { state.Token = next; state.Offset = 0; }
            return Batch(result, state, finished, finished
                ? "Reached the end of the currently accessible uploads. Daily checks continue; private or removed videos are excluded."
                : "Continuing through older public uploads. Only metadata and original video links are imported.");
        }
    }

    public Task<BackfillBatch> CheckYoutubeAsync(Source source, CancellationToken ct)
    {
        if (source.Kind != "youtube") throw new ArgumentException("This health check is for YouTube sources.");
        return YoutubeAsync(source, DateTimeOffset.UtcNow, ct, checkOnly: true);
    }

    public static NormalizedItem? ParseYoutubeUpload(JsonElement entry, string channel)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("snippet", out var snippet) || snippet.ValueKind != JsonValueKind.Object ||
            !entry.TryGetProperty("contentDetails", out var details) || details.ValueKind != JsonValueKind.Object) return null;
        if (!entry.TryGetProperty("status", out var status) || Text(status, "privacyStatus") != "public") return null;
        var id = Text(details, "videoId");
        var title = Text(snippet, "title");
        if (id is null || !Regex.IsMatch(id, @"^[A-Za-z0-9_-]{11}$") || string.IsNullOrWhiteSpace(title) || title is "Deleted video" or "Private video") return null;
        if (Text(snippet, "videoOwnerChannelId") is { } owner && owner != channel) return null;
        if (Text(snippet, "channelId") is { } playlistOwner && playlistOwner != channel) return null;
        // snippet.publishedAt is when it was added to the playlist, not the video's publication date.
        var published = DateTimeOffset.TryParse(Text(details, "videoPublishedAt"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : (DateTimeOffset?)null;
        string? image = null;
        if (snippet.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Object)
            foreach (var name in new[] { "medium", "default", "high" })
                if (thumbnails.TryGetProperty(name, out var thumbnail) && PublicCatalogPolicy.SafeUrl(Text(thumbnail, "url")) is { } url) { image = url; break; }
        return new NormalizedItem
        {
            Kind = "video",
            Provider = "youtube",
            ProviderId = id,
            CanonicalUrl = "https://www.youtube.com/watch?v=" + id,
            Title = ProviderAdapters.Plain(title, 400),
            Author = ProviderAdapters.Plain(Text(snippet, "videoOwnerChannelTitle") ?? Text(snippet, "channelTitle") ?? "", 400),
            Excerpt = ProviderAdapters.Plain(Text(snippet, "description") ?? "", 1200),
            PublishedAt = published,
            ImageUrl = image,
            EmbedUrl = "https://www.youtube-nocookie.com/embed/" + id,
            TextProvenance = "metadata",
            MetadataJson = JsonSerializer.Serialize(new { discoveryProvider = "YouTube Data API uploads playlist", channelId = channel })
        };
    }

    private static JsonElement RequiredItems(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new FormatException("The provider returned no valid items list.");
        return items;
    }
    private static string? Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    public static async Task<bool> IsKnownAsync(AppDbContext db, Source source, NormalizedItem item, CancellationToken ct)
    {
        var keys = ItemCatalog.Identities(item.CanonicalUrl, item.Provider, item.ProviderId, item.Doi, item.ArxivId, source.Id, source.IdentityVersion)
            .Select(i => i.Key).ToArray();
        if (await db.ItemIdentities.AsNoTracking().AnyAsync(i => keys.Contains(i.Key), ct)) return true;
        // Legacy records may predate identity registration. Include hidden and merged aliases.
        return await db.Items.IgnoreQueryFilters().AsNoTracking().AnyAsync(i => i.CanonicalUrl == item.CanonicalUrl ||
            (item.ProviderId != null && (item.Provider == "youtube" || item.Provider == "openalex" || item.Provider == "arxiv" || item.Provider == "soundcloud") &&
                i.Provider == item.Provider && i.ProviderId == item.ProviderId) ||
            (item.Doi != null && i.Doi == item.Doi) || (item.ArxivId != null && i.ArxivId == item.ArxivId), ct);
    }

    public static Task<bool> IsKnownUrlAsync(AppDbContext db, Source source, string url, CancellationToken ct)
    {
        var id = ProviderAdapters.YoutubeVideoId(url);
        return IsKnownAsync(db, source, new NormalizedItem { CanonicalUrl = url, Provider = id is null ? "rss" : "youtube", ProviderId = id }, ct);
    }
}
