using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;

namespace RabbitHole.Ingestion;

/// <summary>Small durable metadata batches from yt-dlp; media is never requested.</summary>
public sealed class YoutubeArchiveBackfill(AppDbContext db, IYtDlpClient client)
{
    private const int PageSize = 30;
    private sealed class Cursor
    {
        public string Engine { get; set; } = "youtube-yt-dlp-v1";
        public string ChannelId { get; set; } = "";
        public int NextStart { get; set; } = 1;
        public DateTimeOffset? Through { get; set; }
        public bool Exhausted { get; set; }
        public string Version { get; set; } = "";
        public List<YtDlpEntry> Pending { get; set; } = [];
    }

    private static string Channel(Source source) => ProviderAdapters.YoutubeChannelId(source.Url)
        ?? throw new YtDlpException("YouTube archive needs a valid channel URL.", "configuration");

    public async Task<BackfillBatch> CheckAsync(Source source, CancellationToken ct)
    {
        if (source.Kind != "youtube") throw new ArgumentException("This health check is for YouTube sources.");
        var channel = Channel(source);
        var state = ReadCursor(source, channel, DateTimeOffset.UtcNow);
        // Probe the saved position too, so a shallow success cannot conceal a deep scan limit.
        var page = await client.FetchAsync(channel, state.Pending.Count > 0 || state.Exhausted ? 1 : state.NextStart, 1, ct);
        return new([], source.BackfillStateJson, "ready", $"yt-dlp {page.Version} health check passed: YouTube answered the metadata request. No items were imported." + Warning(page));
    }

    public async Task<BackfillBatch> FetchAsync(Source source, DateTimeOffset now, CancellationToken ct)
    {
        var channel = Channel(source);
        var state = ReadCursor(source, channel, now);
        YtDlpPage checkedPage;
        // A cached batch must not conceal a broken installation or provider connection for weeks.
        if (state.Pending.Count > 0)
            checkedPage = await client.FetchAsync(channel, 1, 1, ct);
        else
        {
            if (state.Exhausted) { state.NextStart = 1; state.Through = now.AddDays(-30); state.Exhausted = false; }
            checkedPage = await client.FetchAsync(channel, state.NextStart, PageSize, ct);
            state.Pending = checkedPage.Entries.Select(entry => entry with
            {
                Title = ProviderAdapters.Plain(entry.Title ?? "", 400),
                Author = ProviderAdapters.Plain(entry.Author ?? "", 400),
                Description = ProviderAdapters.Plain(entry.Description ?? "", 1200)
            }).ToList();
            state.NextStart = checkedPage.NextStart;
            state.Exhausted = checkedPage.Exhausted;
        }
        state.Version = checkedPage.Version;
        var items = new List<NormalizedItem>();
        while (state.Pending.Count > 0 && items.Count < Math.Clamp(source.BackfillLimit, 1, 10))
        {
            var entry = state.Pending[0];
            state.Pending.RemoveAt(0);
            var item = Parse(entry, channel, state.Version);
            if (item is null || item.PublishedAt > state.Through || items.Any(i => i.ProviderId == item.ProviderId) ||
                await SourceBackfill.IsKnownAsync(db, source, item, ct)) continue;
            items.Add(item);
        }
        var finished = state.Exhausted && state.Pending.Count == 0;
        var message = finished
            ? $"yt-dlp {state.Version} is working. Reached the end of the available public uploads; daily checks continue."
            : $"yt-dlp {state.Version} is working. Continuing through public upload metadata without an API key.";
        return new(items, JsonSerializer.Serialize(state), finished ? "caught-up" : "ready", message + Warning(checkedPage));
    }

    private static Cursor ReadCursor(Source source, string channel, DateTimeOffset now)
    {
        Cursor? state = null;
        if (!string.IsNullOrWhiteSpace(source.BackfillStateJson))
        {
            if (source.BackfillStateJson.Length > 1_000_000) throw new YtDlpException("YouTube archive progress is too large to resume safely.", "cursor");
            try { state = JsonSerializer.Deserialize<Cursor>(source.BackfillStateJson, new JsonSerializerOptions { MaxDepth = 10 }); }
            catch (JsonException) { throw new YtDlpException("YouTube archive progress is unreadable. Check the stored archive state before retrying.", "cursor"); }
        }
        if (state is null || state.Engine != "youtube-yt-dlp-v1" || state.ChannelId != channel)
            state = new Cursor { ChannelId = channel };
        state.Through ??= now.AddDays(-30);
        if (state.Pending is null || state.Pending.Count > PageSize || state.Pending.Any(e => e is null) || state.NextStart is < 1 or > 200_050 ||
            state.Through > now || state.Through < DateTimeOffset.Parse("2000-01-01T00:00:00Z", CultureInfo.InvariantCulture))
            throw new YtDlpException("YouTube archive progress is invalid and could not be resumed.", "cursor");
        if (state.NextStart > 200_000 && !state.Exhausted && state.Pending.Count == 0)
            throw new YtDlpException("YouTube archive reached the supported scan limit. Existing items and recent refreshes remain available.", "request-limit");
        return state;
    }

    private static string Warning(YtDlpPage page) => page.Warnings.Count == 0 ? "" :
        " yt-dlp also reported a metadata warning; some optional fields may be unavailable.";

    public static NormalizedItem? Parse(YtDlpEntry entry, string channel, string version)
    {
        if (!Regex.IsMatch(entry.Id ?? "", @"^[A-Za-z0-9_-]{11}$") || string.IsNullOrWhiteSpace(entry.Title) ||
            entry.Title is "[Private video]" or "[Deleted video]" or "Private video" or "Deleted video" ||
            entry.ChannelId != channel || entry.IsLive || entry.IsUpcoming ||
            entry.Availability is not (null or "public")) return null;
        var published = DateTimeOffset.TryParseExact(entry.UploadDate, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var date) ? date : (DateTimeOffset?)null;
        return new NormalizedItem
        {
            Provider = "youtube",
            ProviderId = entry.Id,
            Kind = "video",
            CanonicalUrl = "https://www.youtube.com/watch?v=" + entry.Id,
            EmbedUrl = "https://www.youtube-nocookie.com/embed/" + entry.Id,
            Title = ProviderAdapters.Plain(entry.Title, 400),
            Author = ProviderAdapters.Plain(entry.Author ?? "", 400),
            Excerpt = ProviderAdapters.Plain(entry.Description ?? "", 1200),
            PublishedAt = published,
            ImageUrl = PublicCatalogPolicy.SafeUrl(entry.Thumbnail),
            TextProvenance = "metadata",
            MetadataJson = JsonSerializer.Serialize(new
            {
                discoveryProvider = "yt-dlp public uploads",
                channelId = channel,
                ytDlpVersion = version,
                publicationDatePrecision = published.HasValue ? "day" : null
            })
        };
    }
}
