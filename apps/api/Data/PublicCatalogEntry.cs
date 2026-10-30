namespace RabbitHole.Data;

// Persist only the bounded public contract. Hidden eligible items remain indexed so
// visibility changes and alias resolution never require a catalog-wide rebuild.
public sealed class PublicCatalogEntry
{
    public Guid Id { get; set; }
    public bool IsPublic { get; set; }
    public Guid? MergedIntoId { get; set; }
    public string Title { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
    public string? Author { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int? PublishedDay { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Type { get; set; } = "";
    public string Provider { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string TypeKey { get; set; } = "";
    public string SearchText { get; set; } = "";
    public string? Excerpt { get; set; }
    public string? EmbedUrl { get; set; }
    public string? ImageUrl { get; set; }
    public List<PublicCatalogTopic> Topics { get; set; } = [];
}

public sealed class PublicCatalogTopic
{
    public Guid ItemId { get; set; }
    public Guid TopicId { get; set; }
    public string Name { get; set; } = "";
}
