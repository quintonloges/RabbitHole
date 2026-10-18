using System.Text.Json;
using RabbitHole.Domain;

namespace RabbitHole.Features;

public sealed record RecommendationEvidence(Guid ItemId, string Title, string Relationship, string? Concept = null, string? Field = null, string? Match = null);
public sealed record RankingSignal(string Name, double Value, string Detail);

public sealed record RecommendationRationale
{
    public int Version { get; init; } = 2;
    public string Kind { get; init; } = "catalog";
    public string Summary { get; init; } = "From your imported collection";
    public string? Direction { get; init; }
    public string? Detail { get; init; }
    public List<RecommendationEvidence> Evidence { get; init; } = [];
    public List<RankingSignal> Signals { get; init; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static RecommendationRationale? FromJson(string? json)
    {
        try { return json is null ? null : JsonSerializer.Deserialize<RecommendationRationale>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
    public static RecommendationRationale LegacyEdition(ContentItem item, IEnumerable<Topic> topics) => new()
    {
        Kind = "legacy-edition",
        Summary = "In your saved edition" + (topics.FirstOrDefault(t => item.TopicIds.Contains(t.Id)) is { } topic ? $" · {topic.Name}" : ""),
        Detail = "This selection predates recorded ranking evidence. Its order is preserved; New selection uses the current recommendations."
    };
}

public sealed record Recommendation(ContentItem Item, RecommendationRationale Rationale, bool Revisit = false)
{
    public string Reason => Rationale.Summary;
    public List<Guid> Evidence => Rationale.Evidence.Select(x => x.ItemId).Where(id => id != Item.Id).Distinct().ToList();
}
