using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;

namespace RabbitHole.Features;

public static class CoreEndpoints
{
    private static readonly SemaphoreSlim EditionLock = new(1, 1);
    public static IEndpointRouteBuilder MapCoreEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/topics", async (AppDbContext db, CancellationToken ct) => await db.Topics.AsNoTracking().OrderByDescending(t => t.Pinned).ThenBy(t => t.Name).ToListAsync(ct));
        api.MapGet("/concepts", () => Concepts.Catalog.Select(c => new { c.Id, c.Label }));
        api.MapPost("/topics", CreateTopic);
        api.MapPatch("/topics/{id:guid}", UpdateTopic);
        api.MapDelete("/topics/{id:guid}", DeleteTopic);
        api.MapGet("/items", ListItems);
        api.MapGet("/items/{id:guid}", GetItem);
        api.MapPatch("/items/{id:guid}", UpdateItem);
        api.MapPatch("/items/{id:guid}/state", UpdateState);
        api.MapPatch("/items/{id:guid}/publication", UpdatePublication);
        api.MapPost("/items/{id:guid}/notes", AddNote);
        api.MapPatch("/notes/{id:guid}", UpdateNote);
        api.MapDelete("/notes/{id:guid}", DeleteNote);
        api.MapGet("/items/{id:guid}/related", Related);
        api.MapGet("/search", Search);
        api.MapGet("/discover/today", Today);
        api.MapPost("/discover/today", CreateEdition);
        api.MapPost("/discover/refresh-edition", RefreshEdition);
        api.MapPost("/discover/more", More);
        api.MapGet("/discover/surprise", Surprise);
        api.MapGet("/holes", ListHoles);
        api.MapPost("/holes", CreateHole);
        api.MapGet("/holes/{id:guid}", GetHole);
        api.MapPost("/holes/{id:guid}/nodes", AddNode);
        api.MapPost("/holes/{id:guid}/navigate", Navigate);
        api.MapPatch("/holes/{id:guid}", UpdateHole);
        api.MapDelete("/holes/{id:guid}", DeleteHole);
        api.MapGet("/practice/prompts", ListPrompts);
        api.MapGet("/practice/due", DuePrompts);
        api.MapPost("/practice/prompts", CreatePrompt);
        api.MapPost("/practice/prompts/{id:guid}/attempts", AddAttempt);
        api.MapGet("/settings", async (AppDbContext db, CancellationToken ct) => await SettingsService.ReadAsync(db, ct));
        api.MapPatch("/settings", UpdateSettings);
        api.MapGet("/system", SystemInfo);
        api.MapGet("/bootstrap/status", SystemInfo);
        api.MapPost("/export", Export);
        return app;
    }

    private static IResult Bad(string detail, int status = 400) => Results.Problem(title: status == 404 ? "Not found" : "Invalid request", detail: detail, statusCode: status);
    private static bool Has(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out _);
    private static bool TextOrNull(JsonElement e, string key) => !Has(e, key) || e.GetProperty(key).ValueKind is JsonValueKind.String or JsonValueKind.Null;
    private static string? String(JsonElement e, string key) => Has(e, key) && e.GetProperty(key).ValueKind == JsonValueKind.String ? e.GetProperty(key).GetString() : null;
    private static bool? Bool(JsonElement e, string key) => Has(e, key) && (e.GetProperty(key).ValueKind is JsonValueKind.True or JsonValueKind.False) ? e.GetProperty(key).GetBoolean() : null;
    private static double? Number(JsonElement e, string key) => Has(e, key) && e.GetProperty(key).ValueKind == JsonValueKind.Number && e.GetProperty(key).TryGetDouble(out var value) && double.IsFinite(value) ? value : null;
    private static int? Integer(JsonElement e, string key) => Has(e, key) && e.GetProperty(key).ValueKind == JsonValueKind.Number && e.GetProperty(key).TryGetInt32(out var value) ? value : null;
    private static List<Guid>? Guids(JsonElement e, string key)
    {
        if (!Has(e, key)) return null;
        if (e.GetProperty(key).ValueKind != JsonValueKind.Array) return null;
        var ids = new List<Guid>();
        foreach (var value in e.GetProperty(key).EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var id)) return null;
            ids.Add(id);
        }
        return ids.Distinct().ToList();
    }

    private static async Task<IResult> CreateTopic(JsonElement body, AppDbContext db, CancellationToken ct)
    {
        if (!TextOrNull(body, "description") || !TextOrNull(body, "keywords") || !TextOrNull(body, "aliases")) return Bad("Topic text fields must be strings or null.");
        if (Has(body, "weight") && (Integer(body, "weight") is not int weight || weight < 0 || weight > 5)) return Bad("Weight must be an integer between 0 and 5.");
        foreach (var key in new[] { "pinned", "curiosityOnly", "enabled" }) if (Has(body, key) && Bool(body, key) == null) return Bad($"{key} must be a boolean.");
        var name = String(body, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) return Bad("A topic name of at most 120 characters is required.");
        if (await db.Topics.AnyAsync(t => t.Name == name, ct)) return Bad("Topic name already exists.", 409);
        var topic = new Topic { Name = name, Description = String(body, "description") ?? "", Keywords = String(body, "keywords") ?? "", Aliases = String(body, "aliases") ?? "", Weight = Integer(body, "weight") ?? 3, Pinned = Bool(body, "pinned") ?? false, CuriosityOnly = Bool(body, "curiosityOnly") ?? false, Enabled = Bool(body, "enabled") ?? true };
        db.Topics.Add(topic);
        await db.SaveChangesAsync(ct);
        await TopicClassification.ReclassifyAsync(db, ct);
        return Results.Created($"/api/topics/{topic.Id}", topic);
    }
    private static async Task<IResult> UpdateTopic(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var topic = await db.Topics.FindAsync([id], ct);
        if (topic == null) return Bad("Topic does not exist.", 404);
        if (!TextOrNull(body, "description") || !TextOrNull(body, "keywords") || !TextOrNull(body, "aliases")) return Bad("Topic text fields must be strings or null.");
        foreach (var key in new[] { "pinned", "curiosityOnly", "enabled" }) if (Has(body, key) && Bool(body, key) == null) return Bad($"{key} must be a boolean.");
        if (Has(body, "name")) { var name = String(body, "name")?.Trim(); if (string.IsNullOrWhiteSpace(name) || name.Length > 120) return Bad("Invalid topic name."); topic.Name = name; }
        if (Has(body, "description")) topic.Description = String(body, "description") ?? "";
        if (Has(body, "keywords")) topic.Keywords = String(body, "keywords") ?? "";
        if (Has(body, "aliases")) topic.Aliases = String(body, "aliases") ?? "";
        if (Has(body, "weight")) { var weight = Integer(body, "weight"); if (weight == null || weight < 0 || weight > 5) return Bad("Weight must be an integer between 0 and 5."); topic.Weight = weight.Value; }
        if (Has(body, "pinned")) topic.Pinned = Bool(body, "pinned") ?? topic.Pinned;
        if (Has(body, "curiosityOnly")) topic.CuriosityOnly = Bool(body, "curiosityOnly") ?? topic.CuriosityOnly;
        if (Has(body, "enabled")) topic.Enabled = Bool(body, "enabled") ?? topic.Enabled;
        await db.SaveChangesAsync(ct);
        await TopicClassification.ReclassifyAsync(db, ct);
        return Results.Ok(topic);
    }
    private static async Task<IResult> DeleteTopic(Guid id, AppDbContext db, CancellationToken ct)
    {
        var topic = await db.Topics.FindAsync([id], ct);
        if (topic == null) return Bad("Topic does not exist.", 404);
        foreach (var item in await db.Items.ToListAsync(ct)) if (item.TopicIds.Contains(id)) item.TopicIds = item.TopicIds.Where(topicId => topicId != id).ToList();
        foreach (var source in await db.Sources.ToListAsync(ct)) if (source.TopicIds.Contains(id)) source.TopicIds = source.TopicIds.Where(topicId => topicId != id).ToList();
        db.Topics.Remove(topic);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListItems(HttpRequest request, AppDbContext db, CancellationToken ct)
    {
        var query = db.Items.AsNoTracking().AsQueryable();
        if (request.Query.ContainsKey("isPublic"))
        {
            if (!bool.TryParse(request.Query["isPublic"], out var isPublic)) return Bad("isPublic must be true or false.");
            query = query.Where(i => i.IsPublic == isPublic);
        }
        if (Guid.TryParse(request.Query["sourceId"], out var sourceId)) query = query.Where(i => i.SourceId == sourceId || db.ItemSources.Any(s => s.ItemId == i.Id && s.SourceKey == sourceId));
        if (request.Query.TryGetValue("kind", out var kind) && !string.IsNullOrWhiteSpace(kind)) query = query.Where(i => i.Kind == kind.ToString());
        var all = (await query.ToListAsync(ct)).OrderByDescending(i => i.IngestedAt).ToList();
        if (Guid.TryParse(request.Query["topicId"], out var topicId)) all = all.Where(i => i.TopicIds.Contains(topicId)).ToList();
        var states = await db.States.AsNoTracking().ToDictionaryAsync(s => s.ItemId, ct);
        if (bool.TryParse(request.Query["saved"], out var saved)) all = all.Where(i => states.GetValueOrDefault(i.Id)?.Saved == saved).ToList();
        if (bool.TryParse(request.Query["read"], out var read)) all = all.Where(i => (states.GetValueOrDefault(i.Id)?.Read ?? false) == read).ToList();
        if (bool.TryParse(request.Query["opened"], out var opened)) all = all.Where(i => (states.GetValueOrDefault(i.Id)?.LastOpenedAt != null) == opened).ToList();
        if (bool.TryParse(request.Query["dismissed"], out var dismissed)) all = all.Where(i => (states.GetValueOrDefault(i.Id)?.Dismissed ?? false) == dismissed).ToList();
        if (request.Query["order"] == "reading")
        {
            var documentIds = all.Where(i => i.DocumentId.HasValue).Select(i => i.DocumentId!.Value).Distinct().ToList();
            var itemIds = all.Select(i => i.Id).ToList();
            var imported = await db.Documents.AsNoTracking().Where(d => documentIds.Contains(d.Id))
                .Select(d => new { d.Id, d.CreatedAt }).ToDictionaryAsync(d => d.Id, d => d.CreatedAt, ct);
            var positions = await db.Sections.AsNoTracking().Where(s => itemIds.Contains(s.ItemId))
                .Select(s => new { s.ItemId, s.Order }).ToDictionaryAsync(s => s.ItemId, s => s.Order, ct);
            // A book is one import. Extraction timestamps must not put its index ahead of its chapters.
            all = all.OrderByDescending(i => i.DocumentId.HasValue && imported.TryGetValue(i.DocumentId.Value, out var added) ? added : i.IngestedAt)
                .ThenBy(i => i.DocumentId ?? i.Id).ThenBy(i => positions.GetValueOrDefault(i.Id, int.MaxValue)).ThenBy(i => i.Id).ToList();
        }
        var total = all.Count;
        var offset = Math.Clamp(int.TryParse(request.Query["offset"], out var o) ? o : 0, 0, 100000);
        var limit = Math.Clamp(int.TryParse(request.Query["limit"], out var l) ? l : 30, 1, 100);
        var views = await ItemDto.BuildAsync(db, all.Skip(offset).Take(limit), ct);
        if (bool.TryParse(request.Query["notes"], out var includeNotes) && includeNotes)
        {
            var ids = views.Select(i => i.Id).ToList();
            var notes = (await db.Notes.AsNoTracking().Where(n => ids.Contains(n.ItemId)).ToListAsync(ct)).OrderBy(n => n.CreatedAt).ToList();
            foreach (var view in views) view.Notes = notes.Where(n => n.ItemId == view.Id).ToList();
        }
        return Results.Ok(new { items = views, total });
    }
    private static async Task<IResult> GetItem(Guid id, AppDbContext db, Ranking ranking, CancellationToken ct)
    {
        var item = await ItemCatalog.ResolveAsync(db, id, ct);
        if (item == null) return Bad("Item does not exist.", 404);
        id = item.Id;
        var dto = (await ItemDto.BuildAsync(db, [item], ct, includeBody: true))[0];
        dto.Notes = (await db.Notes.AsNoTracking().Where(n => n.ItemId == id).ToListAsync(ct)).OrderBy(n => n.CreatedAt).ToList();
        var related = await ranking.RelatedAsync(item, 8, ct);
        dto.Related = await ItemDto.RecommendationsAsync(db, related, ct);
        return Results.Ok(dto);
    }
    private static async Task<IResult> UpdateItem(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var item = await ItemCatalog.ResolveAsync(db, id, ct);
        if (item == null) return Bad("Item does not exist.", 404);
        if (!TextOrNull(body, "author")) return Bad("author must be text or null.");
        foreach (var key in new[] { "conceptTags", "excludedConcepts" })
        {
            if (!Has(body, key)) continue;
            var value = body.GetProperty(key);
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 32 || value.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()) || v.GetString()!.Length > 80))
                return Bad($"{key} must contain at most 32 nonempty tags of at most 80 characters.");
            var tags = value.EnumerateArray().Select(v => Concepts.TagId(v.GetString()!)).Where(t => t.Length > 0).Distinct().ToList();
            if (key == "conceptTags") item.ConceptTags = tags; else item.ExcludedConcepts = tags;
        }
        if (item.ConceptTags.Intersect(item.ExcludedConcepts).Any()) return Bad("A concept cannot be both included and excluded.");
        if (Has(body, "title")) { var title = String(body, "title")?.Trim(); if (string.IsNullOrWhiteSpace(title)) return Bad("Title is required."); item.Title = title; }
        if (Has(body, "author")) item.Author = String(body, "author");
        if (Has(body, "topicIds"))
        {
            var ids = Guids(body, "topicIds");
            if (ids == null || await db.Topics.CountAsync(t => ids.Contains(t.Id), ct) != ids.Count) return Bad("Every topic ID must refer to an existing topic.");
            await TopicClassification.SetUserTopicsAsync(db, item, ids, ct);
        }
        else await TopicClassification.AssignAsync(db, item, [], ct);
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok((await ItemDto.BuildAsync(db, [item], ct, includeBody: true))[0]);
    }
    private static async Task<IResult> UpdatePublication(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        if (Bool(body, "isPublic") is not bool isPublic) return Bad("isPublic must be a boolean.");
        var requested = await db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (requested == null) return Bad("Item does not exist.", 404);
        if (isPublic && !await PublicCatalogPolicy.CanPublishAsync(db, requested, ct)) return Bad("Document-derived items and private or credential-bearing URLs cannot be published.");
        var item = await ItemCatalog.ResolveAsync(db, id, ct);
        if (item == null) return Bad("Item does not exist.", 404);
        if (isPublic && !await PublicCatalogPolicy.CanPublishAsync(db, item, ct)) return Bad("Document-derived items and private or credential-bearing URLs cannot be published.");
        requested.IsPublic = isPublic;
        item.IsPublic = isPublic;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok((await ItemDto.BuildAsync(db, [item], ct))[0]);
    }
    private static async Task<IResult> UpdateState(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var item = await ItemCatalog.ResolveAsync(db, id, ct);
        if (item is null) return Bad("Item does not exist.", 404);
        id = item.Id;
        var state = await db.States.FindAsync([id], ct);
        if (state == null) { state = new ItemState { ItemId = id }; db.States.Add(state); }
        var now = DateTimeOffset.UtcNow;
        if (Has(body, "saved")) { var value = Bool(body, "saved"); if (value == null) return Bad("saved must be a boolean."); state.Saved = value.Value; state.SavedAt = value.Value ? now : null; }
        if (Has(body, "read")) { var value = Bool(body, "read"); if (value == null) return Bad("read must be a boolean."); state.Read = value.Value; state.ReadAt = value.Value ? now : null; }
        if (Has(body, "dismissed")) { var value = Bool(body, "dismissed"); if (value == null) return Bad("dismissed must be a boolean."); state.Dismissed = value.Value; }
        if (Has(body, "understood")) { var value = Bool(body, "understood"); if (value == null) return Bad("understood must be a boolean."); state.Understood = value.Value; }
        if (Has(body, "opened")) { var value = Bool(body, "opened"); if (value == null) return Bad("opened must be a boolean."); if (value.Value) state.LastOpenedAt = now; }
        if (Has(body, "progress")) { var value = Number(body, "progress"); if (value == null || value < 0 || value > 1) return Bad("progress must be between 0 and 1."); state.Progress = value.Value; }
        if (Has(body, "feedback")) { var value = Integer(body, "feedback"); if (value == null || value < -1 || value > 1) return Bad("feedback must be -1, 0, or 1."); state.Feedback = value.Value; }
        await db.SaveChangesAsync(ct);
        return Results.Ok(state);
    }
    private static async Task<IResult> AddNote(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var item = await ItemCatalog.ResolveAsync(db, id, ct);
        if (item == null) return Bad("Item does not exist.", 404);
        id = item.Id;
        var text = String(body, "body")?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20000) return Bad("Note body must be between 1 and 20,000 characters.");
        if (Has(body, "quote") && body.GetProperty("quote").ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return Bad("quote must be text or null.");
        if (Has(body, "locator") && body.GetProperty("locator").ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return Bad("locator must be text or null.");
        var quote = String(body, "quote")?.Trim();
        var locator = String(body, "locator")?.Trim();
        if (quote?.Length > 4000 || locator?.Length > 500) return Bad("Quote or locator is too long.");
        if (!QuoteMatchesStoredText(item, quote)) return Bad("Quote was not found in the stored source text. Save it in the note body instead.");
        var note = new Note { ItemId = id, Body = text, Quote = quote, Locator = locator };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/notes/{note.Id}", note);
    }
    private static async Task<IResult> UpdateNote(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var note = await db.Notes.FindAsync([id], ct);
        if (note == null) return Bad("Note does not exist.", 404);
        if (Has(body, "body")) { var text = String(body, "body")?.Trim(); if (string.IsNullOrWhiteSpace(text) || text.Length > 20000) return Bad("Invalid note body."); note.Body = text; }
        if (Has(body, "quote"))
        {
            if (body.GetProperty("quote").ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return Bad("quote must be text or null.");
            var quote = String(body, "quote")?.Trim();
            if (quote?.Length > 4000) return Bad("Quote is too long.");
            var item = await db.Items.AsNoTracking().FirstAsync(i => i.Id == note.ItemId, ct);
            if (!QuoteMatchesStoredText(item, quote)) return Bad("Quote was not found in the stored source text. Save it in the note body instead.");
            note.Quote = quote;
        }
        if (Has(body, "locator"))
        {
            if (body.GetProperty("locator").ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return Bad("locator must be text or null.");
            var locator = String(body, "locator")?.Trim();
            if (locator?.Length > 500) return Bad("Locator is too long.");
            note.Locator = locator;
        }
        note.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(note);
    }
    private static async Task<IResult> DeleteNote(Guid id, AppDbContext db, CancellationToken ct)
    {
        var note = await db.Notes.FindAsync([id], ct);
        if (note == null) return Bad("Note does not exist.", 404);
        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
    private static bool QuoteMatchesStoredText(ContentItem item, string? quote)
    {
        if (string.IsNullOrWhiteSpace(quote) || string.IsNullOrWhiteSpace(item.Body)) return true;
        static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Normalize(item.Body).Contains(Normalize(quote), StringComparison.OrdinalIgnoreCase);
    }
    private static async Task<IResult> Related(Guid id, HttpRequest request, AppDbContext db, Ranking ranking, CancellationToken ct)
    {
        var item = await ItemCatalog.ResolveAsync(db, id, ct);
        if (item == null) return Bad("Item does not exist.", 404);
        var visited = new HashSet<Guid>();
        if (request.Query.TryGetValue("holeId", out var holeText))
        {
            if (!Guid.TryParse(holeText, out var holeId)) return Bad("holeId must be a valid ID.");
            if (!await db.Holes.AnyAsync(h => h.Id == holeId, ct)) return Bad("Rabbit hole does not exist.", 404);
            visited.UnionWith(await db.Nodes.Where(n => n.HoleId == holeId && n.ItemId != null).Select(n => n.ItemId!.Value).ToListAsync(ct));
        }
        if (request.Query.TryGetValue("visitedIds", out var visitText))
        {
            var values = visitText.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length > 200 || values.Any(v => !Guid.TryParse(v, out _))) return Bad("visitedIds must contain at most 200 item IDs.");
            visited.UnionWith(values.Select(Guid.Parse));
        }
        visited = (await ItemCatalog.ResolveIdsAsync(db, visited, ct)).Values.ToHashSet();
        var includeVisited = false;
        if (request.Query.TryGetValue("includeVisited", out var includeText) && !bool.TryParse(includeText, out includeVisited)) return Bad("includeVisited must be true or false.");
        var related = await ranking.RelatedAsync(item, 20, ct, visited, includeVisited);
        return Results.Ok(await ItemDto.RecommendationsAsync(db, related, ct));
    }

    private static async Task<IResult> Search(HttpRequest request, AppDbContext db, CancellationToken ct)
    {
        var q = request.Query["q"].ToString().Trim();
        if (q.Length > 200) return Bad("Search is too long.");
        var kind = request.Query["kind"].ToString().Trim();
        var sourceText = request.Query["sourceId"].ToString();
        var topicText = request.Query["topicId"].ToString();
        var savedText = request.Query["saved"].ToString();
        var readText = request.Query["read"].ToString();
        var afterText = (request.Query["publishedAfter"].ToString() is { Length: > 0 } after ? after : request.Query["publishedFrom"].ToString() is { Length: > 0 } from ? from : request.Query["from"].ToString());
        var beforeText = (request.Query["publishedBefore"].ToString() is { Length: > 0 } before ? before : request.Query["publishedTo"].ToString() is { Length: > 0 } to ? to : request.Query["to"].ToString());
        if (sourceText.Length > 0 && !Guid.TryParse(sourceText, out _)) return Bad("sourceId must be a valid ID.");
        if (topicText.Length > 0 && !Guid.TryParse(topicText, out _)) return Bad("topicId must be a valid ID.");
        if (savedText.Length > 0 && !bool.TryParse(savedText, out _)) return Bad("saved must be true or false.");
        if (readText.Length > 0 && !bool.TryParse(readText, out _)) return Bad("read must be true or false.");
        var dateFormat = "yyyy-MM-dd";
        if (afterText.Length > 0 && !DateOnly.TryParseExact(afterText, dateFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) return Bad("publishedAfter must be YYYY-MM-DD.");
        if (beforeText.Length > 0 && !DateOnly.TryParseExact(beforeText, dateFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) return Bad("publishedBefore must be YYYY-MM-DD.");
        var hasFilters = kind.Length > 0 || sourceText.Length > 0 || topicText.Length > 0 || savedText.Length > 0 || readText.Length > 0 || afterText.Length > 0 || beforeText.Length > 0;
        if (q.Length == 0 && !hasFilters) return Results.Ok(new { items = Array.Empty<ItemDto>(), total = 0 });
        List<ContentItem> matches;
        if (q.Length == 0) matches = await db.Items.AsNoTracking().ToListAsync(ct);
        else if (db.Database.IsNpgsql() && q.Length >= 3)
        {
            matches = await db.Items.FromSqlInterpolated($@"SELECT i.* FROM ""Items"" AS i WHERE to_tsvector('simple', coalesce(i.""Title"",'') || ' ' || coalesce(i.""Excerpt"",'') || ' ' || coalesce(i.""Body"",'')) @@ plainto_tsquery('simple', {q}) OR EXISTS (SELECT 1 FROM ""Notes"" n WHERE n.""ItemId"" = i.""Id"" AND to_tsvector('simple', n.""Body"") @@ plainto_tsquery('simple', {q})) OR EXISTS (SELECT 1 FROM ""Sections"" s WHERE s.""ItemId"" = i.""Id"" AND to_tsvector('simple', s.""Text"") @@ plainto_tsquery('simple', {q}))").AsNoTracking().ToListAsync(ct);
        }
        else matches = await db.Items.AsNoTracking().Where(i => i.Title.Contains(q) || (i.Excerpt != null && i.Excerpt.Contains(q)) || (i.Body != null && i.Body.Contains(q)) || db.Notes.Any(n => n.ItemId == i.Id && (n.Body.Contains(q) || (n.Quote != null && n.Quote.Contains(q)))) || db.Sections.Any(s => s.ItemId == i.Id && s.Text.Contains(q))).ToListAsync(ct);
        if (q.Length > 0)
        {
            var topics = await db.Topics.AsNoTracking().ToListAsync(ct);
            var topicIds = topics.Where(t => (t.Name + " " + t.Keywords + " " + t.Aliases).Contains(q, StringComparison.OrdinalIgnoreCase)).Select(t => t.Id).ToHashSet();
            if (topicIds.Count > 0)
            {
                var extra = await db.Items.AsNoTracking().ToListAsync(ct);
                matches.AddRange(extra.Where(i => i.TopicIds.Any(topicIds.Contains) && matches.All(m => m.Id != i.Id)));
            }
            if (db.Database.IsNpgsql())
            {
                var partial = await db.Items.AsNoTracking().Where(i => EF.Functions.ILike(i.Title, $"%{q}%") || (i.Excerpt != null && EF.Functions.ILike(i.Excerpt, $"%{q}%")) || db.Notes.Any(n => n.ItemId == i.Id && (EF.Functions.ILike(n.Body, $"%{q}%") || (n.Quote != null && EF.Functions.ILike(n.Quote, $"%{q}%")))) || db.Sections.Any(s => s.ItemId == i.Id && EF.Functions.ILike(s.Text, $"%{q}%"))).ToListAsync(ct);
                matches.AddRange(partial.Where(i => matches.All(m => m.Id != i.Id)));
            }
        }
        matches = matches.DistinctBy(i => i.Id).ToList();
        if (kind.Length > 0) matches = matches.Where(i => i.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToList();
        if (Guid.TryParse(sourceText, out var sourceId))
        {
            var sourceItems = await db.ItemSources.Where(x => x.SourceKey == sourceId).Select(x => x.ItemId).ToListAsync(ct);
            matches = matches.Where(i => i.SourceId == sourceId || sourceItems.Contains(i.Id)).ToList();
        }
        if (Guid.TryParse(topicText, out var topicId)) matches = matches.Where(i => i.TopicIds.Contains(topicId)).ToList();
        if (DateOnly.TryParseExact(afterText, dateFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var publishedAfter)) matches = matches.Where(i => i.PublishedAt.HasValue && DateOnly.FromDateTime(i.PublishedAt.Value.UtcDateTime) >= publishedAfter).ToList();
        if (DateOnly.TryParseExact(beforeText, dateFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var publishedBefore)) matches = matches.Where(i => i.PublishedAt.HasValue && DateOnly.FromDateTime(i.PublishedAt.Value.UtcDateTime) <= publishedBefore).ToList();
        if (savedText.Length > 0 || readText.Length > 0)
        {
            var itemIds = matches.Select(i => i.Id).ToList();
            var states = await db.States.AsNoTracking().Where(s => itemIds.Contains(s.ItemId)).ToDictionaryAsync(s => s.ItemId, ct);
            if (bool.TryParse(savedText, out var saved)) matches = matches.Where(i => (states.GetValueOrDefault(i.Id)?.Saved ?? false) == saved).ToList();
            if (bool.TryParse(readText, out var read)) matches = matches.Where(i => (states.GetValueOrDefault(i.Id)?.Read ?? false) == read).ToList();
        }
        matches = matches.OrderByDescending(i => i.IngestedAt).ToList();
        var total = matches.Count;
        var offset = Math.Clamp(int.TryParse(request.Query["offset"], out var o) ? o : 0, 0, 100000);
        var limit = Math.Clamp(int.TryParse(request.Query["limit"], out var l) ? l : 30, 1, 100);
        return Results.Ok(new { items = await ItemDto.BuildAsync(db, matches.Skip(offset).Take(limit), ct), total });
    }

    private static async Task<IResult> Today(AppDbContext db, Ranking ranking, CancellationToken ct) => await EditionResult(db, ranking, false, false, ct, readOnly: true);
    private static async Task<IResult> CreateEdition(AppDbContext db, Ranking ranking, CancellationToken ct) => await EditionResult(db, ranking, false, false, ct);
    private static async Task<IResult> RefreshEdition(AppDbContext db, Ranking ranking, CancellationToken ct) => await EditionResult(db, ranking, true, false, ct);
    private static async Task<IResult> More(AppDbContext db, Ranking ranking, CancellationToken ct) => await EditionResult(db, ranking, false, true, ct);
    private static async Task<IResult> EditionResult(AppDbContext db, Ranking ranking, bool refresh, bool more, CancellationToken ct, bool readOnly = false)
    {
        await EditionLock.WaitAsync(ct);
        try
        {
            var day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
            var edition = await db.Editions.SingleOrDefaultAsync(e => e.Day == day, ct);
            if (edition == null && readOnly) return Results.Ok(new { id = (Guid?)null, day, items = Array.Empty<ItemDto>(), total = 0 });
            if (edition == null) { edition = new FeedEdition { Day = day }; db.Editions.Add(edition); await db.SaveChangesAsync(ct); }
            var entries = await db.Entries.Where(e => e.EditionId == edition.Id).OrderBy(e => e.Position).ToListAsync(ct);
            if (refresh && entries.Count > 0) { db.Entries.RemoveRange(entries); await db.SaveChangesAsync(ct); entries.Clear(); }
            var settings = await SettingsService.ReadAsync(db, ct);
            if (!readOnly && (entries.Count == 0 || more))
            {
                var excluded = await ItemCatalog.ResolveIdsAsync(db, entries.Select(e => e.ItemId), ct);
                var selected = await ranking.SelectAsync(Math.Clamp(settings.EditionSize, 5, 100), day + (more ? $":{entries.Count}" : ""), excluded.Values.ToHashSet(), ct);
                foreach (var choice in selected)
                {
                    var entry = new FeedEntry { EditionId = edition.Id, ItemId = choice.Item.Id, Position = entries.Count, Reason = choice.Reason, EvidenceItemIds = choice.Evidence, RationaleJson = choice.Rationale.ToJson() };
                    db.Entries.Add(entry); entries.Add(entry);
                }
                await db.SaveChangesAsync(ct);
            }
            var resolved = await ItemCatalog.ResolveIdsAsync(db, entries.Select(e => e.ItemId), ct);
            var ids = resolved.Values.Distinct().ToList();
            var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
            var byId = items.ToDictionary(i => i.Id);
            var visible = entries.Where(e => resolved.ContainsKey(e.ItemId) && byId.ContainsKey(resolved[e.ItemId]))
                .DistinctBy(e => resolved[e.ItemId]).ToList();
            var ordered = visible.Select(e => byId[resolved[e.ItemId]]).ToList();
            var topics = await db.Topics.AsNoTracking().ToListAsync(ct);
            var recommendations = visible.Select(e => new Recommendation(byId[resolved[e.ItemId]],
                RecommendationRationale.FromJson(e.RationaleJson) ?? RecommendationRationale.LegacyEdition(byId[resolved[e.ItemId]], topics)));
            return Results.Ok(new { edition.Id, edition.Day, items = await ItemDto.RecommendationsAsync(db, recommendations, ct), total = ordered.Count });
        }
        finally { EditionLock.Release(); }
    }
    private static async Task<IResult> Surprise(AppDbContext db, Ranking ranking, CancellationToken ct)
    {
        var selected = await ranking.SelectAsync(20, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd") + ":surprise", null, ct);
        if (selected.Count == 0) return Bad("No imported items are available yet.", 404);
        var index = Random.Shared.Next(selected.Count);
        var choice = selected[index];
        return Results.Ok((await ItemDto.RecommendationsAsync(db, [choice], ct))[0]);
    }

    private static async Task<IResult> ListHoles(AppDbContext db, CancellationToken ct)
    {
        var holes = (await db.Holes.AsNoTracking().ToListAsync(ct)).OrderByDescending(h => h.UpdatedAt).ToList();
        var nodes = await db.Nodes.AsNoTracking().ToListAsync(ct);
        var itemIds = nodes.Where(n => n.ItemId.HasValue).Select(n => n.ItemId!.Value).ToList();
        var titles = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Title, ct);
        return Results.Ok(holes.Select(h => new { h.Id, h.Title, h.Origin, h.OriginNote, h.LastNodeId, h.CreatedAt, h.UpdatedAt, nodeCount = nodes.Count(n => n.HoleId == h.Id), rootTitle = nodes.Where(n => n.HoleId == h.Id && n.ParentId == null).OrderBy(n => n.CreatedAt).Select(n => n.ItemId.HasValue ? titles.GetValueOrDefault(n.ItemId.Value) : n.Question).FirstOrDefault() }));
    }
    private static async Task<IResult> CreateHole(JsonElement body, AppDbContext db, CancellationToken ct)
    {
        if (Has(body, "itemId") && !Guid.TryParse(String(body, "itemId"), out _)) return Bad("itemId must be a valid item ID.");
        var itemId = Guid.TryParse(String(body, "itemId"), out var parsed) ? parsed : (Guid?)null;
        var question = String(body, "question")?.Trim();
        if (itemId == null && string.IsNullOrWhiteSpace(question)) return Bad("An existing item or your own question is required.");
        ContentItem? item = null;
        if (itemId.HasValue) { item = await ItemCatalog.ResolveAsync(db, itemId.Value, ct); if (item == null) return Bad("Item does not exist.", 404); itemId = item.Id; }
        var title = String(body, "title")?.Trim();
        if (string.IsNullOrWhiteSpace(title)) title = item?.Title ?? question!;
        if (title.Length > 200 || question?.Length > 2000) return Bad("Title or question is too long.");
        var origin = String(body, "origin") ?? "user";
        if (Has(body, "origin") && String(body, "origin") == null) return Bad("origin must be text.");
        if (origin is not ("user" or "curated" or "example")) return Bad("origin must be user, curated, or example.");
        var hole = new RabbitHole.Domain.RabbitHole { Title = title, Origin = origin };
        var node = new RabbitNode { HoleId = hole.Id, ItemId = itemId, Question = itemId == null ? question : null, Origin = origin, Rationale = origin == "user" ? "Chosen by you" : "Curated starting point" };
        hole.LastNodeId = node.Id;
        db.Holes.Add(hole); db.Nodes.Add(node);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/holes/{hole.Id}", new { hole.Id, hole.Title, hole.Origin, hole.OriginNote, hole.LastNodeId, hole.CreatedAt, hole.UpdatedAt, nodes = new[] { node } });
    }
    private static async Task<IResult> GetHole(Guid id, AppDbContext db, CancellationToken ct)
    {
        var hole = await db.Holes.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct);
        if (hole == null) return Bad("Rabbit hole does not exist.", 404);
        var nodes = (await db.Nodes.AsNoTracking().Where(n => n.HoleId == id).ToListAsync(ct)).OrderBy(n => n.CreatedAt).ToList();
        var ids = nodes.Where(n => n.ItemId.HasValue).Select(n => n.ItemId!.Value).ToList();
        var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var dtos = (await ItemDto.BuildAsync(db, items, ct)).ToDictionary(i => i.Id);
        return Results.Ok(new { hole.Id, hole.Title, hole.Origin, hole.OriginNote, hole.LastNodeId, hole.CreatedAt, hole.UpdatedAt, nodes = nodes.Select(n => new { n.Id, n.HoleId, n.ParentId, n.ItemId, n.Question, n.Origin, n.Rationale, n.EvidenceItemIds, n.CreatedAt, item = n.ItemId.HasValue ? dtos.GetValueOrDefault(n.ItemId.Value) : null }) });
    }
    private static async Task<IResult> AddNode(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var hole = await db.Holes.FindAsync([id], ct);
        if (hole == null) return Bad("Rabbit hole does not exist.", 404);
        if (!Guid.TryParse(String(body, "parentId"), out var parentId) || !await db.Nodes.AnyAsync(n => n.Id == parentId && n.HoleId == id, ct)) return Bad("Parent node must exist in this rabbit hole.");
        if (Has(body, "itemId") && !Guid.TryParse(String(body, "itemId"), out _)) return Bad("itemId must be a valid item ID.");
        var itemId = Guid.TryParse(String(body, "itemId"), out var parsed) ? parsed : (Guid?)null;
        var question = String(body, "question")?.Trim();
        if (itemId == null && string.IsNullOrWhiteSpace(question)) return Bad("An existing item or your own question is required.");
        if (itemId is Guid requestedId)
        {
            var item = await ItemCatalog.ResolveAsync(db, requestedId, ct);
            if (item is null) return Bad("Item does not exist.", 404);
            itemId = item.Id;
        }
        if (question?.Length > 2000) return Bad("Question is too long.");
        var rationale = String(body, "rationale")?.Trim() ?? "Chosen by you";
        if (rationale.Length > 500) return Bad("Rationale is too long.");
        // Curators can build a whole example without accidentally creating user
        // evidence. Interactive clients explicitly send origin=user for new turns.
        var origin = String(body, "origin") ?? (hole.Origin is "curated" or "example" ? hole.Origin : "user");
        if (Has(body, "origin") && String(body, "origin") == null) return Bad("origin must be text.");
        if (origin is not ("user" or "curated" or "example")) return Bad("origin must be user, curated, or example.");
        var node = new RabbitNode { HoleId = id, ParentId = parentId, ItemId = itemId, Question = itemId == null ? question : null, Rationale = origin != "user" && !Has(body, "rationale") ? "Curated branch" : rationale, Origin = origin };
        if (itemId.HasValue)
        {
            var parent = await db.Nodes.FindAsync([parentId], ct);
            if (parent?.ItemId.HasValue == true) node.EvidenceItemIds = [parent.ItemId.Value];
        }
        db.Nodes.Add(node); hole.LastNodeId = node.Id; hole.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/holes/{id}", node);
    }
    private static async Task<IResult> Navigate(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var hole = await db.Holes.FindAsync([id], ct);
        if (hole == null) return Bad("Rabbit hole does not exist.", 404);
        if (!Guid.TryParse(String(body, "nodeId"), out var nodeId) || !await db.Nodes.AnyAsync(n => n.Id == nodeId && n.HoleId == id, ct)) return Bad("Node does not exist in this rabbit hole.");
        hole.LastNodeId = nodeId; hole.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(hole);
    }
    private static async Task<IResult> UpdateHole(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var hole = await db.Holes.FindAsync([id], ct);
        if (hole == null) return Bad("Rabbit hole does not exist.", 404);
        var title = String(body, "title")?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) return Bad("A title of at most 200 characters is required.");
        hole.Title = title; hole.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(hole);
    }
    private static async Task<IResult> DeleteHole(Guid id, AppDbContext db, CancellationToken ct)
    {
        var hole = await db.Holes.FindAsync([id], ct);
        if (hole == null) return Bad("Rabbit hole does not exist.", 404);
        db.Nodes.RemoveRange(db.Nodes.Where(n => n.HoleId == id)); db.Holes.Remove(hole);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListPrompts(AppDbContext db, CancellationToken ct) => Results.Ok(await PromptsWithContext(db, false, ct));
    private static async Task<IResult> DuePrompts(AppDbContext db, CancellationToken ct) => Results.Ok(await PromptsWithContext(db, true, ct));
    private static async Task<object[]> PromptsWithContext(AppDbContext db, bool due, CancellationToken ct)
    {
        var prompts = (await db.Prompts.AsNoTracking().ToListAsync(ct)).OrderBy(p => p.DueAt).ToList();
        if (due) prompts = prompts.Where(p => p.DueAt <= DateTimeOffset.UtcNow).ToList();
        var ids = prompts.Select(p => p.ItemId).ToList();
        var itemTitles = await db.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Title, ct);
        var promptIds = prompts.Select(p => p.Id).ToList();
        var attempts = (await db.Attempts.AsNoTracking().Where(a => promptIds.Contains(a.PromptId)).ToListAsync(ct)).OrderBy(a => a.CreatedAt).ToList();
        return prompts.Select(p => (object)new { p.Id, p.ItemId, p.Question, p.Reference, p.Kind, p.Provenance, p.DueAt, p.IntervalDays, p.Ease, p.Reviews, itemTitle = itemTitles.GetValueOrDefault(p.ItemId), attempts = attempts.Where(a => a.PromptId == p.Id).ToList() }).ToArray();
    }
    private static async Task<IResult> CreatePrompt(JsonElement body, AppDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(String(body, "itemId"), out var itemId)) return Bad("An existing item ID is required.");
        var item = await ItemCatalog.ResolveAsync(db, itemId, ct);
        if (item == null) return Bad("Item does not exist.", 404);
        itemId = item.Id;
        var kind = String(body, "kind") ?? "reflection";
        if (Has(body, "kind") && body.GetProperty("kind").ValueKind != JsonValueKind.String) return Bad("Practice kind must be text.");
        if (kind is not ("reflection" or "recall" or "compare" or "reasoning" or "architecture")) return Bad("Practice kind must be reflection, recall, compare, reasoning, or architecture.");
        var question = kind switch
        {
            "recall" => $"What do you remember about “{item.Title}”?",
            "compare" => $"Which other source you have read would you compare with “{item.Title}”, and why?",
            "reasoning" => $"What reasoning in “{item.Title}” would you examine more closely?",
            "architecture" => $"What design decision would you make after reading “{item.Title}”, and what tradeoff would you consider?",
            _ => $"What idea from “{item.Title}” is worth revisiting?"
        };
        var reference = item.Body ?? item.Excerpt;
        if (reference?.Length > 1200) reference = reference[..1200];
        var prompt = new PracticePrompt { ItemId = itemId, Kind = kind, Question = question, Reference = reference, Provenance = "deterministic-source-reflection" };
        db.Prompts.Add(prompt);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/practice/prompts/{prompt.Id}", new { prompt.Id, prompt.ItemId, prompt.Question, prompt.Reference, prompt.Kind, prompt.Provenance, prompt.DueAt, prompt.IntervalDays, prompt.Ease, prompt.Reviews, itemTitle = item.Title, attempts = Array.Empty<PracticeAttempt>() });
    }
    private static async Task<IResult> AddAttempt(Guid id, JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var prompt = await db.Prompts.FindAsync([id], ct);
        if (prompt == null) return Bad("Practice prompt does not exist.", 404);
        var rating = String(body, "rating") ?? "";
        if (!new[] { "again", "hard", "good", "easy", "skip" }.Contains(rating)) return Bad("Rating must be again, hard, good, easy, or skip.");
        var response = String(body, "response") ?? "";
        if (response.Length > 20000) return Bad("Response is too long.");
        var attempt = new PracticeAttempt { PromptId = id, Response = response, Rating = rating };
        db.Attempts.Add(attempt);
        var now = DateTimeOffset.UtcNow;
        if (rating == "skip") prompt.DueAt = now.AddDays(1);
        else
        {
            prompt.Reviews++;
            prompt.Ease = Math.Clamp(prompt.Ease + (rating == "again" ? -0.2 : rating == "hard" ? -0.1 : rating == "easy" ? 0.15 : 0), 1.3, 3.5);
            prompt.IntervalDays = rating == "again" ? 0.25 : prompt.Reviews == 1 ? 1 : prompt.Reviews == 2 ? 3 : Math.Clamp(prompt.IntervalDays * prompt.Ease * (rating == "hard" ? 0.65 : rating == "easy" ? 1.3 : 1), 1, 365);
            prompt.DueAt = now.AddDays(prompt.IntervalDays);
        }
        await db.SaveChangesAsync(ct);
        var title = await db.Items.AsNoTracking().Where(i => i.Id == prompt.ItemId).Select(i => i.Title).FirstAsync(ct);
        var attempts = (await db.Attempts.AsNoTracking().Where(a => a.PromptId == id).ToListAsync(ct)).OrderBy(a => a.CreatedAt).ToList();
        return Results.Ok(new { prompt.Id, prompt.ItemId, prompt.Question, prompt.Reference, prompt.Kind, prompt.Provenance, prompt.DueAt, prompt.IntervalDays, prompt.Ease, prompt.Reviews, itemTitle = title, attempts });
    }

    private static async Task<IResult> UpdateSettings(JsonElement body, AppDbContext db, CancellationToken ct)
    {
        var settings = await SettingsService.ReadAsync(db, ct);
        if (Has(body, "editionSize")) { var value = Integer(body, "editionSize"); if (value == null || value < 5 || value > 100) return Bad("Edition size must be an integer between 5 and 100."); settings.EditionSize = value.Value; }
        if (Has(body, "serendipity")) { var value = Number(body, "serendipity"); if (value == null || value < 0 || value > 1) return Bad("Serendipity must be between 0 and 1."); settings.Serendipity = value.Value; }
        if (Has(body, "freshness")) { var value = Number(body, "freshness"); if (value == null || value < 0 || value > 1) return Bad("Freshness must be between 0 and 1."); settings.Freshness = value.Value; }
        if (Has(body, "archiveShare")) { var value = Number(body, "archiveShare"); if (value == null || value < 0 || value > 0.5) return Bad("Archive share must be between 0 and 0.5."); settings.ArchiveShare = value.Value; }
        if (Has(body, "practiceEnabled")) { var value = Bool(body, "practiceEnabled"); if (value == null) return Bad("practiceEnabled must be a boolean."); settings.PracticeEnabled = value.Value; }
        if (Has(body, "ingestionPaused")) { var value = Bool(body, "ingestionPaused"); if (value == null) return Bad("ingestionPaused must be a boolean."); settings.IngestionPaused = value.Value; }
        if (Has(body, "aiEnabled")) { var value = Bool(body, "aiEnabled"); if (value == null) return Bad("aiEnabled must be a boolean."); settings.AiEnabled = value.Value; }
        if (Has(body, "theme")) { var value = String(body, "theme"); if (value is not ("system" or "dark" or "light")) return Bad("Theme must be system, dark, or light."); settings.Theme = value; }
        await SettingsService.SaveAsync(db, settings, ct);
        return Results.Ok(settings);
    }
    private static async Task<IResult> SystemInfo(AppDbContext db, IConfiguration config, CancellationToken ct)
    {
        return Results.Ok(new { status = "ok", counts = new { topics = await db.Topics.CountAsync(ct), sources = await db.Sources.CountAsync(ct), items = await db.Items.CountAsync(ct), books = await db.Documents.CountAsync(ct), holes = await db.Holes.CountAsync(ct) }, configuration = new { aiConfigured = !string.IsNullOrWhiteSpace(config["AI:BaseUrl"]) && !string.IsNullOrWhiteSpace(config["AI:Model"]) && (!string.IsNullOrWhiteSpace(config["AI:ApiKey"]) || config.GetValue<bool>("AI:AllowLocal")), openAlexConfigured = !string.IsNullOrWhiteSpace(config["OpenAlex:ApiKey"] ?? config["OPENALEX_API_KEY"]), youtubeConfigured = !string.IsNullOrWhiteSpace(config["YOUTUBE_API_KEY"]) } });
    }
    private static async Task<IResult> Export(AppDbContext db, CancellationToken ct)
    {
        var sources = (await db.Sources.AsNoTracking().ToListAsync(ct)).Select(s => new { s.Id, s.IdentityVersion, s.Name, s.Kind, url = ExportSanitizer.RedactUrl(s.Url), s.Query, s.TopicIds, s.Enabled, s.IntervalMinutes, s.ResultLimit, s.LastSuccessAt, s.LastAttemptAt, s.NextSyncAt, s.FailureCount, s.LastImportedCount }).ToList();
        var documents = (await db.Documents.AsNoTracking().ToListAsync(ct)).Select(d => new { d.Id, d.Title, d.Author, d.FileName, d.MimeType, d.SizeBytes, d.Sha256, d.Status, d.PageCount, d.CreatedAt }).ToList();
        // Explicit export contracts prevent new persistence fields from accidentally exposing credentials.
        var items = (await db.Items.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct)).Select(i => new
        {
            i.Id,
            i.SourceId,
            i.IsPublic,
            i.MergedIntoId,
            i.Kind,
            i.Title,
            i.Author,
            canonicalUrl = ExportSanitizer.RedactUrl(i.CanonicalUrl),
            i.Provider,
            providerId = ExportSanitizer.RedactIdentifier(i.ProviderId),
            i.Doi,
            i.ArxivId,
            i.Excerpt,
            i.Body,
            i.TextProvenance,
            imageUrl = i.ImageUrl is null ? null : ExportSanitizer.RedactUrl(i.ImageUrl),
            embedUrl = i.EmbedUrl is null ? null : ExportSanitizer.RedactUrl(i.EmbedUrl),
            metadataJson = ExportSanitizer.RedactMetadata(i.MetadataJson),
            i.ImportWarning,
            i.PublishedAt,
            i.IngestedAt,
            i.UpdatedAt,
            i.TopicIds,
            i.ClassificationOrigin,
            i.ConceptTags,
            i.ExcludedConcepts,
            i.DocumentId,
            i.SectionId
        }).ToList();
        var identities = (await db.ItemIdentities.AsNoTracking().ToListAsync(ct)).Select(x => new
        { x.Key, x.ItemId, x.Kind, scope = ExportSanitizer.RedactIdentifier(x.Scope), value = ExportSanitizer.RedactIdentifier(x.Value) }).ToList();
        var itemSources = (await db.ItemSources.AsNoTracking().ToListAsync(ct)).Select(x => new
        { x.Id, x.ItemId, x.SourceId, x.SourceKey, x.SourceVersion, x.Name, x.Kind, url = ExportSanitizer.RedactUrl(x.Url), x.TopicIds, x.LastSeenAt }).ToList();
        var export = new { version = 3, exportedAt = DateTimeOffset.UtcNow, topics = await db.Topics.AsNoTracking().ToListAsync(ct), sources, items, identities, itemSources, itemTopics = await db.ItemTopics.AsNoTracking().ToListAsync(ct), states = await db.States.AsNoTracking().ToListAsync(ct), notes = await db.Notes.AsNoTracking().ToListAsync(ct), documents, sections = await db.Sections.AsNoTracking().ToListAsync(ct), holes = await db.Holes.AsNoTracking().ToListAsync(ct), nodes = await db.Nodes.AsNoTracking().ToListAsync(ct), prompts = await db.Prompts.AsNoTracking().ToListAsync(ct), attempts = await db.Attempts.AsNoTracking().ToListAsync(ct), editions = await db.Editions.AsNoTracking().ToListAsync(ct), entries = await db.Entries.AsNoTracking().ToListAsync(ct), settings = await SettingsService.ReadAsync(db, ct) };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new UtcDateTimeOffsetConverter());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(export, options);
        return Results.File(bytes, "application/json", $"rabbithole-export-{DateTimeOffset.UtcNow:yyyy-MM-dd}.json");
    }

}
