using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;

namespace RabbitHole.Data;

/// <summary>Maintains the public read model before the same SaveChanges as its inputs.</summary>
public static class PublicCatalogIndex
{
    private const string VersionKey = "public-catalog-index-v1";
    private static readonly Expression<Func<ContentItem, ContentItem>> PublicFields = i => new ContentItem
    {
        Id = i.Id,
        IsPublic = i.IsPublic,
        MergedIntoId = i.MergedIntoId,
        DocumentId = i.DocumentId,
        SectionId = i.SectionId,
        TextProvenance = i.TextProvenance,
        SourceId = i.SourceId,
        TopicIds = i.TopicIds,
        Title = i.Title,
        CanonicalUrl = i.CanonicalUrl,
        Author = i.Author,
        PublishedAt = i.PublishedAt,
        IngestedAt = i.IngestedAt,
        UpdatedAt = i.UpdatedAt,
        Kind = i.Kind,
        Provider = i.Provider,
        Excerpt = i.Excerpt,
        EmbedUrl = i.EmbedUrl,
        ImageUrl = i.ImageUrl
    };
    private static readonly HashSet<string> PublicFieldNames = ((MemberInitExpression)PublicFields.Body).Bindings.Select(b => b.Member.Name).ToHashSet();

    public static bool HasChanges(AppDbContext db) => db.ChangeTracker.Entries().Any(e =>
        e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && (e.Entity switch
        {
            ContentItem or ItemSource => true,
            Source or Topic => e.State != EntityState.Modified || e.Property("Name").IsModified,
            DocumentSection => e.State != EntityState.Modified || e.Property("ItemId").IsModified,
            LibraryDocument => e.State == EntityState.Deleted,
            _ => false
        }));

    // All writers take this lock inside their transaction before reading dependencies.
    // SQLite's write transaction already serializes writers; PostgreSQL needs an
    // explicit transaction lock to prevent a concurrent topic/source edit going stale.
    internal static async Task LockAsync(AppDbContext db, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(793216461)", ct);
    }

    public static async Task BackfillAsync(AppDbContext db, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(db, ct);
        if (!await db.Settings.AnyAsync(s => s.Key == VersionKey, ct))
        {
            // A one-time upgrade; ordinary reads never repair or rebuild the index.
            var ids = (await db.Items.IgnoreQueryFilters().Select(i => i.Id).ToListAsync(ct)).ToHashSet();
            await PrepareAsync(db, ct, ids);
            db.Settings.Add(new AppSetting { Key = VersionKey, Value = "complete" });
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public static async Task PrepareAsync(AppDbContext db, CancellationToken ct, HashSet<Guid>? rebuild = null)
    {
        var changedItems = db.ChangeTracker.Entries<ContentItem>().Where(Changed).ToList();
        var changedSources = db.ChangeTracker.Entries<Source>().Where(Changed).ToList();
        var changedLinks = db.ChangeTracker.Entries<ItemSource>().Where(Changed).ToList();
        var changedTopics = db.ChangeTracker.Entries<Topic>().Where(Changed).ToList();
        var changedSections = db.ChangeTracker.Entries<DocumentSection>().Where(Changed).ToList();
        var deletedDocuments = db.ChangeTracker.Entries<LibraryDocument>().Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToHashSet();
        var ids = rebuild ?? changedItems.Select(e => e.Entity.Id).ToHashSet();
        foreach (var entry in changedItems)
        {
            if (entry.Entity.MergedIntoId is Guid parent) ids.Add(parent);
            if (entry.OriginalValues.GetValue<Guid?>(nameof(ContentItem.MergedIntoId)) is Guid oldParent) ids.Add(oldParent);
        }
        foreach (var entry in changedLinks)
        { ids.Add(entry.Entity.ItemId); ids.Add(entry.OriginalValues.GetValue<Guid>(nameof(ItemSource.ItemId))); }
        foreach (var entry in changedSections)
        { ids.Add(entry.Entity.ItemId); ids.Add(entry.OriginalValues.GetValue<Guid>(nameof(DocumentSection.ItemId))); }
        if (changedLinks.Count > 0)
        {
            var linkIds = changedLinks.Select(e => e.Entity.Id).ToList();
            ids.UnionWith(await db.ItemSources.Where(s => linkIds.Contains(s.Id)).Select(s => s.ItemId).ToListAsync(ct));
        }
        if (changedSections.Count > 0)
        {
            var sectionIds = changedSections.Select(e => e.Entity.Id).ToList();
            ids.UnionWith(await db.Sections.Where(s => sectionIds.Contains(s.Id)).Select(s => s.ItemId).ToListAsync(ct));
        }
        if (deletedDocuments.Count > 0)
            ids.UnionWith(await db.Sections.Where(s => deletedDocuments.Contains(s.DocumentId)).Select(s => s.ItemId).ToListAsync(ct));
        if (changedSources.Count > 0)
        {
            var sourceIds = changedSources.Select(e => e.Entity.Id).ToList();
            ids.UnionWith(await db.Items.IgnoreQueryFilters().Where(i => i.SourceId.HasValue && sourceIds.Contains(i.SourceId.Value)).Select(i => i.Id).ToListAsync(ct));
        }
        if (changedTopics.Count > 0)
        {
            // TopicIds is a legacy JSON column. Only topic edits scan this small
            // projection; public queries use the indexed relational topic table.
            var topicIds = changedTopics.Select(e => e.Entity.Id).ToHashSet();
            var memberships = await db.Items.IgnoreQueryFilters().Select(i => new { i.Id, i.TopicIds }).ToListAsync(ct);
            ids.UnionWith(memberships.Where(i => i.TopicIds.Any(topicIds.Contains)).Select(i => i.Id));
        }
        if (ids.Count == 0) return;

        // Expand only affected alias components, including old merge targets. Overlay
        // pending changes because both originals and projections are saved together.
        Dictionary<Guid, ContentItem> items;
        int previousCount;
        do
        {
            previousCount = ids.Count;
            items = (await db.Items.IgnoreQueryFilters().AsNoTracking()
                .Where(i => ids.Contains(i.Id) || (i.MergedIntoId.HasValue && ids.Contains(i.MergedIntoId.Value)))
                .Select(PublicFields).ToListAsync(ct)).ToDictionary(i => i.Id);
            ids.UnionWith(items.Values.Where(i => i.MergedIntoId.HasValue).Select(i => i.MergedIntoId!.Value));
            foreach (var entry in changedItems)
                if (entry.State == EntityState.Deleted) items.Remove(entry.Entity.Id);
                else if (entry.State == EntityState.Added) items[entry.Entity.Id] = entry.Entity;
                else if (items.TryGetValue(entry.Entity.Id, out var stored))
                    foreach (var property in entry.Properties.Where(p => p.IsModified && PublicFieldNames.Contains(p.Metadata.Name)))
                        property.Metadata.PropertyInfo!.SetValue(stored, property.CurrentValue);
            ids.UnionWith(items.Keys);
            ids.UnionWith(items.Values.Where(i => i.MergedIntoId.HasValue).Select(i => i.MergedIntoId!.Value));
        } while (ids.Count != previousCount);

        var sections = await db.Sections.AsNoTracking().Where(s => ids.Contains(s.ItemId))
            .Select(s => new DocumentSection { Id = s.Id, ItemId = s.ItemId, DocumentId = s.DocumentId }).ToListAsync(ct);
        Overlay(sections, changedSections, s => s.Id);
        sections.RemoveAll(s => deletedDocuments.Contains(s.DocumentId));
        var blocked = sections.Select(s => s.ItemId).ToHashSet();
        foreach (var item in items.Values)
            if (item.DocumentId.HasValue || item.SectionId.HasValue || PublicCatalogPolicy.IsDocumentMetadata(item.Kind, item.Provider, item.TextProvenance)) blocked.Add(item.Id);
        var edges = items.Values.Where(i => i.MergedIntoId.HasValue).SelectMany(i =>
            new[] { (From: i.Id, To: i.MergedIntoId!.Value), (From: i.MergedIntoId!.Value, To: i.Id) }).ToLookup(e => e.From, e => e.To);
        var queue = new Queue<Guid>(blocked);
        while (queue.TryDequeue(out var id))
            foreach (var neighbor in edges[id]) if (blocked.Add(neighbor)) queue.Enqueue(neighbor);

        var links = await db.ItemSources.AsNoTracking().Where(s => ids.Contains(s.ItemId))
            .Select(s => new ItemSource { Id = s.Id, ItemId = s.ItemId, Name = s.Name }).ToListAsync(ct);
        Overlay(links, changedLinks, s => s.Id);
        var directIds = items.Values.Where(i => i.SourceId.HasValue).Select(i => i.SourceId!.Value).ToHashSet();
        var sources = await db.Sources.AsNoTracking().Where(s => directIds.Contains(s.Id))
            .Select(s => new Source { Id = s.Id, Name = s.Name }).ToListAsync(ct);
        Overlay(sources, changedSources, s => s.Id);
        var directNames = sources.ToDictionary(s => s.Id, s => s.Name);
        var names = links.ToLookup(s => s.ItemId, s => s.Name);
        var topics = await db.Topics.AsNoTracking().Select(t => new Topic { Id = t.Id, Name = t.Name }).ToListAsync(ct);
        Overlay(topics, changedTopics, t => t.Id);
        var topicNames = topics.ToDictionary(t => t.Id, t => ProviderAdapters.Plain(t.Name, 200));
        // A context may have saved before another writer changed these projections.
        // Reload their original values under the lock, just like the source fields.
        foreach (var tracked in db.ChangeTracker.Entries<PublicCatalogTopic>().Where(e => ids.Contains(e.Entity.ItemId)).ToList()) tracked.State = EntityState.Detached;
        foreach (var tracked in db.ChangeTracker.Entries<PublicCatalogEntry>().Where(e => ids.Contains(e.Entity.Id)).ToList()) tracked.State = EntityState.Detached;
        var existing = await db.PublicCatalogEntries.Where(i => ids.Contains(i.Id)).Include(i => i.Topics).ToDictionaryAsync(i => i.Id, ct);
        foreach (var id in ids)
        {
            existing.TryGetValue(id, out var projection);
            if (!items.TryGetValue(id, out var item) || blocked.Contains(id) || !PublicCatalogPolicy.IsEligible(item))
            {
                if (projection != null) db.PublicCatalogEntries.Remove(projection);
                continue;
            }
            if (projection == null) { projection = new PublicCatalogEntry { Id = id }; db.PublicCatalogEntries.Add(projection); }
            projection.IsPublic = item.IsPublic;
            projection.MergedIntoId = item.MergedIntoId;
            projection.Title = ProviderAdapters.Plain(item.Title, 400);
            projection.CanonicalUrl = PublicCatalogPolicy.SafeUrl(item.CanonicalUrl)!;
            projection.Author = item.Author == null ? null : ProviderAdapters.Plain(item.Author, 200);
            projection.PublishedAt = item.PublishedAt;
            projection.PublishedDay = item.PublishedAt.HasValue ? DateOnly.FromDateTime(item.PublishedAt.Value.UtcDateTime).DayNumber : null;
            projection.IngestedAt = item.IngestedAt;
            projection.UpdatedAt = item.UpdatedAt;
            projection.Type = ProviderAdapters.Plain(item.Kind, 40);
            projection.Provider = ProviderAdapters.Plain(item.Provider, 40);
            var sourceNames = names[id].ToList();
            if (item.SourceId is Guid sourceId && directNames.TryGetValue(sourceId, out var name)) sourceNames.Add(name);
            var joined = string.Join(", ", sourceNames.Select(n => ProviderAdapters.Plain(n, 200)).Distinct().Order(StringComparer.Ordinal));
            projection.SourceName = sourceNames.Count == 0 ? projection.Provider : joined[..Math.Min(600, joined.Length)];
            projection.SourceKey = projection.SourceName.ToUpperInvariant();
            projection.TypeKey = projection.Type.ToUpperInvariant();
            projection.Excerpt = item.Excerpt == null ? null : ProviderAdapters.Plain(item.Excerpt, 600);
            projection.EmbedUrl = PublicCatalogPolicy.SafeEmbed(item.EmbedUrl);
            projection.ImageUrl = PublicCatalogPolicy.SafeUrl(item.ImageUrl);
            var wanted = item.TopicIds.Where(topicNames.ContainsKey).ToHashSet();
            foreach (var old in projection.Topics.Where(t => !wanted.Contains(t.TopicId)).ToList())
            { projection.Topics.Remove(old); db.PublicCatalogTopics.Remove(old); }
            foreach (var topicId in wanted)
            {
                var topic = projection.Topics.FirstOrDefault(t => t.TopicId == topicId);
                if (topic == null) { topic = new PublicCatalogTopic { ItemId = id, TopicId = topicId }; projection.Topics.Add(topic); }
                topic.Name = topicNames[topicId];
            }
            projection.SearchText = string.Join(' ', new[] { projection.Title, projection.Author, projection.Excerpt,
                projection.SourceName, projection.Type, projection.Provider, projection.CanonicalUrl }
                .Concat(projection.Topics.OrderBy(t => t.Name).Select(t => t.Name))).ToUpperInvariant();
        }
    }

    private static bool Changed<T>(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T> entry) where T : class =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    private static void Overlay<T>(List<T> records, IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T>> changes, Func<T, Guid> id) where T : class
    {
        foreach (var entry in changes)
        {
            if (entry.State == EntityState.Deleted) records.RemoveAll(record => id(record) == id(entry.Entity));
            else if (entry.State == EntityState.Added) records.Add(entry.Entity);
            else if (records.FirstOrDefault(record => id(record) == id(entry.Entity)) is { } stored)
                foreach (var property in entry.Properties.Where(p => p.IsModified))
                    property.Metadata.PropertyInfo!.SetValue(stored, property.CurrentValue);
        }
    }
}
