using System.Text.Json;

namespace RabbitHole.Ingestion;

public sealed record SourcePreset(string Name, string Kind, string Url, string[] Topics, int IntervalMinutes, int ResultLimit, string Reason, string Query = "");

public static class SourcePresets
{
    public static readonly IReadOnlyList<SourcePreset> All = Load();

    private static List<SourcePreset> Load()
    {
        using var stream = typeof(SourcePresets).Assembly.GetManifestResourceStream("RabbitHole.curated-source-pack.json")
            ?? throw new InvalidOperationException("The bundled source catalog is missing.");
        using var document = JsonDocument.Parse(stream);
        var presets = document.RootElement.GetProperty("sources").EnumerateArray()
            .Where(source => source.GetProperty("enabledDefault").GetBoolean() && source.GetProperty("kind").GetString() is "rss" or "web-archive")
            .Select(source => new SourcePreset(source.GetProperty("name").GetString()!, source.GetProperty("kind").GetString()!, source.GetProperty("url").GetString()!,
                source.GetProperty("topics").EnumerateArray().Select(topic => topic.GetString()!).ToArray(),
                source.GetProperty("intervalMinutes").GetInt32(), source.GetProperty("resultLimit").GetInt32(), source.GetProperty("reason").GetString()!, source.GetProperty("query").GetString()!))
            .ToList();
        // Retain existing starter choices even when not enabled by the curated pack.
        presets.Add(new("OpenAI News", "rss", "https://openai.com/news/rss.xml", ["Frontier AI"], 1440, 15, "First-party announcements."));
        presets.Add(new("arXiv cs.AI", "rss", "https://export.arxiv.org/rss/cs.AI", ["Frontier AI"], 1440, 25, "New AI paper metadata."));
        return presets.DistinctBy(p => (p.Kind, p.Url, p.Query)).OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
    }
}
