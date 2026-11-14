namespace RabbitHole.Features;

/// <summary>A rolling selection budget, independent of publication volume or viewpoint.</summary>
public sealed class DiscoveryBalance(IReadOnlyDictionary<Guid, double>? weights = null)
{
    public const int WindowSize = 24;
    public const int PublisherLimit = 2;
    private readonly Queue<(string Publisher, Guid[] Topics)> window = new();
    private readonly Dictionary<string, int> publishers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, double> topics = new();

    public int PublisherCount(string publisher) => publishers.GetValueOrDefault(publisher);

    public double TopicLoad(IEnumerable<Guid> ids)
    {
        var keys = TopicKeys(ids);
        // A multi-topic item spends one slot, shared across its topics.
        return keys.Average(id => topics.GetValueOrDefault(id) / Math.Max(0.25, weights?.GetValueOrDefault(id, 1) ?? 1));
    }

    public List<T> SpreadPublishers<T>(IEnumerable<T> candidates, Func<T, string> publisher)
    {
        var list = candidates.ToList();
        var belowLimit = list.Where(c => PublisherCount(publisher(c)) < PublisherLimit).ToList();
        if (belowLimit.Count > 0) return belowLimit;
        // Sparse catalogs and explicit source filters still return real items.
        var minimum = list.Select(c => PublisherCount(publisher(c))).DefaultIfEmpty(0).Min();
        return list.Where(c => PublisherCount(publisher(c)) == minimum).ToList();
    }

    public void Add(string publisher, IEnumerable<Guid> ids)
    {
        var keys = TopicKeys(ids);
        window.Enqueue((publisher, keys));
        publishers[publisher] = PublisherCount(publisher) + 1;
        foreach (var id in keys) topics[id] = topics.GetValueOrDefault(id) + 1.0 / keys.Length;
        if (window.Count <= WindowSize) return;
        var previous = window.Dequeue();
        publishers[previous.Publisher]--;
        foreach (var id in previous.Topics) topics[id] -= 1.0 / previous.Topics.Length;
    }

    private Guid[] TopicKeys(IEnumerable<Guid> ids)
    {
        var keys = ids.Distinct().Where(id => weights == null || weights.ContainsKey(id)).Order().ToArray();
        return keys.Length == 0 ? [Guid.Empty] : keys;
    }

    public static string Publisher(string url, string fallback, string? author = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return fallback;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        // Shared media hosts contain independent creators. Ordinary websites are
        // grouped by host so another feed cannot evade the publisher budget.
        if (host is "youtube.com" or "youtu.be" or "youtube-nocookie.com" or "vimeo.com")
            return "creator:" + (string.IsNullOrWhiteSpace(author) ? fallback : author.Trim().ToUpperInvariant());
        if (host == "soundcloud.com") return host + "/" + uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return host;
    }
}
