using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;

namespace RabbitHole.Features;

public sealed class ItemDto : ContentItem
{
    public ItemState State { get; set; } = new();
    public List<Topic> Topics { get; set; } = [];
    public string SourceName { get; set; } = "";
    public List<ItemSource> Sources { get; set; } = [];
    public string? Reason { get; set; }
    public List<Guid> EvidenceItemIds { get; set; } = [];
    public List<Note>? Notes { get; set; }
    public List<ItemDto>? Related { get; set; }

    public static async Task<List<ItemDto>> BuildAsync(AppDbContext db, IEnumerable<ContentItem> items, CancellationToken ct, IReadOnlyDictionary<Guid, (string Reason, List<Guid> Evidence)>? reasons = null, bool includeBody = false)
    {
        var list = items.ToList();
        var ids = list.Select(i => i.Id).ToList();
        var sourceIds = list.Where(i => i.SourceId.HasValue).Select(i => i.SourceId!.Value).Distinct().ToList();
        var states = await db.States.AsNoTracking().Where(s => ids.Contains(s.ItemId)).ToDictionaryAsync(s => s.ItemId, ct);
        var sources = await db.Sources.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var topics = await db.Topics.AsNoTracking().ToListAsync(ct);
        var memberships = (await db.ItemSources.AsNoTracking().Where(x => ids.Contains(x.ItemId)).ToListAsync(ct)).ToLookup(x => x.ItemId);
        return list.Select(i =>
        {
            var dto = new ItemDto
            {
                Id = i.Id,
                SourceId = i.SourceId,
                Kind = i.Kind,
                Title = i.Title,
                Author = i.Author,
                CanonicalUrl = i.CanonicalUrl,
                Provider = i.Provider,
                ProviderId = i.ProviderId,
                Doi = i.Doi,
                ArxivId = i.ArxivId,
                Excerpt = i.Excerpt,
                Body = includeBody ? i.Body : null,
                TextProvenance = i.TextProvenance,
                ImageUrl = i.ImageUrl,
                EmbedUrl = i.EmbedUrl,
                MetadataJson = i.MetadataJson,
                ImportWarning = i.ImportWarning,
                PublishedAt = i.PublishedAt,
                IngestedAt = i.IngestedAt,
                UpdatedAt = i.UpdatedAt,
                TopicIds = i.TopicIds,
                ClassificationOrigin = i.ClassificationOrigin,
                DocumentId = i.DocumentId,
                SectionId = i.SectionId,
                State = states.GetValueOrDefault(i.Id) ?? new ItemState { ItemId = i.Id },
                Topics = topics.Where(t => i.TopicIds.Contains(t.Id)).ToList(),
                Sources = memberships[i.Id].ToList(),
                SourceName = memberships[i.Id].Any() ? string.Join(", ", memberships[i.Id].Select(x => x.Name).Distinct()) :
                    i.SourceId.HasValue && sources.TryGetValue(i.SourceId.Value, out var source) ? source.Name : i.Provider
            };
            if (reasons != null && reasons.TryGetValue(i.Id, out var reason))
            {
                dto.Reason = reason.Reason;
                dto.EvidenceItemIds = reason.Evidence;
            }
            return dto;
        }).ToList();
    }
}
