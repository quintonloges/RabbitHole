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
    public RecommendationRationale? Rationale { get; set; }
    public List<ConceptMatch> Concepts { get; set; } = [];
    public bool Revisit { get; set; }
    public bool HasText { get; set; }
    public ItemDocumentContext? Document { get; set; }

    public static async Task<List<ItemDto>> RecommendationsAsync(AppDbContext db, IEnumerable<Recommendation> recommendations, CancellationToken ct)
    {
        var list = recommendations.ToList();
        var dtos = await BuildAsync(db, list.Select(r => r.Item), ct);
        var byId = list.ToDictionary(r => r.Item.Id);
        foreach (var dto in dtos)
        {
            var recommendation = byId[dto.Id];
            dto.Reason = recommendation.Reason;
            dto.EvidenceItemIds = recommendation.Evidence;
            dto.Rationale = recommendation.Rationale;
            dto.Revisit = recommendation.Revisit;
        }
        return dtos;
    }

    public static async Task<List<ItemDto>> BuildAsync(AppDbContext db, IEnumerable<ContentItem> items, CancellationToken ct, IReadOnlyDictionary<Guid, (string Reason, List<Guid> Evidence)>? reasons = null, bool includeBody = false)
    {
        var list = items.ToList();
        var ids = list.Select(i => i.Id).ToList();
        var sourceIds = list.Where(i => i.SourceId.HasValue).Select(i => i.SourceId!.Value).Distinct().ToList();
        var states = await db.States.AsNoTracking().Where(s => ids.Contains(s.ItemId)).ToDictionaryAsync(s => s.ItemId, ct);
        var sources = await db.Sources.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var topics = await db.Topics.AsNoTracking().ToListAsync(ct);
        var memberships = (await db.ItemSources.AsNoTracking().Where(x => ids.Contains(x.ItemId)).ToListAsync(ct)).ToLookup(x => x.ItemId);
        var documentIds = list.Where(i => i.DocumentId.HasValue).Select(i => i.DocumentId!.Value).Distinct().ToList();
        var sectionIds = list.Where(i => i.SectionId.HasValue).Select(i => i.SectionId!.Value).Distinct().ToList();
        // List responses identify the parent and exact kind of section without copying private files or full text.
        var documents = await db.Documents.AsNoTracking().Where(d => documentIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Title, d.Author, d.MimeType, d.PageCount }).ToDictionaryAsync(d => d.Id, ct);
        var sections = await db.Sections.AsNoTracking().Where(s => sectionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DocumentId, s.Title, s.Order, s.StartPage, s.EndPage, s.Locator, s.IsSuperseded }).ToDictionaryAsync(s => s.Id, ct);
        return list.Select(i =>
        {
            ItemDocumentContext? document = null;
            if (i.DocumentId is Guid documentId && documents.TryGetValue(documentId, out var parent))
            {
                var section = i.SectionId.HasValue ? sections.GetValueOrDefault(i.SectionId.Value) : null;
                if (section?.DocumentId != documentId) section = null;
                var isPage = section?.Locator?.StartsWith("page=", StringComparison.OrdinalIgnoreCase) == true ||
                    section is { Locator: null, StartPage: not null } && section.StartPage == section.EndPage && section.Title == $"Page {section.StartPage}";
                document = new(parent.Id, parent.Title, parent.Author,
                    parent.MimeType == "application/pdf" ? "PDF" : parent.MimeType == "application/epub+zip" ? "EPUB" : "Document",
                    section == null || section.Locator == "original" ? "document" : isPage ? "page" : "section",
                    section?.StartPage, section?.EndPage, parent.PageCount, section?.IsSuperseded ?? false, section?.Order + 1);
            }
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
                ConceptTags = i.ConceptTags,
                ExcludedConcepts = i.ExcludedConcepts,
                Concepts = Features.Concepts.Match(i),
                ClassificationOrigin = i.ClassificationOrigin,
                DocumentId = i.DocumentId,
                SectionId = i.SectionId,
                HasText = !string.IsNullOrWhiteSpace(i.Body),
                Document = document,
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

public sealed record ItemDocumentContext(Guid Id, string Title, string? Author, string Format, string SectionType,
    int? StartPage, int? EndPage, int PageCount, bool IsSuperseded, int? SectionNumber = null);
