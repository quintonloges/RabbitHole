using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;

namespace RabbitHole.Features;

public static class PublicCatalogEndpoints
{
    public static IEndpointRouteBuilder MapPublicCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/public").AllowAnonymous();
        api.MapGet("/items", List);
        api.MapGet("/items/{id:guid}", Detail);
        api.MapGet("/items/{id:guid}/related", Related);
        api.MapGet("/filters", Filters);
        api.MapGet("/sitemap", Sitemap);
        return app;
    }

    private static IResult Invalid(string detail) => Results.Problem(statusCode: 400, title: "Invalid request", detail: detail);
    private static async Task<IResult> List(HttpRequest request, HttpResponse response, AppDbContext db, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var q = request.Query["q"].ToString().Trim();
        if (q.Length > 200) return Invalid("Search is limited to 200 characters.");
        var page = 1;
        var pageSize = 24;
        if (request.Query.ContainsKey("page") && (!int.TryParse(request.Query["page"], out page) || page is < 1 or > 100000)) return Invalid("page must be between 1 and 100000.");
        if (request.Query.ContainsKey("pageSize") && (!int.TryParse(request.Query["pageSize"], out pageSize) || pageSize is < 1 or > 100)) return Invalid("pageSize must be between 1 and 100.");
        Guid? topicId = null;
        if (request.Query.ContainsKey("topicId") && !string.IsNullOrEmpty(request.Query["topicId"]))
        { if (!Guid.TryParse(request.Query["topicId"], out var parsed)) return Invalid("topicId must be a valid ID."); topicId = parsed; }
        DateOnly? from = null, to = null;
        foreach (var key in new[] { "from", "to" })
        {
            var text = request.Query[key].ToString();
            if (text.Length == 0) continue;
            if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return Invalid($"{key} must be YYYY-MM-DD.");
            if (key == "from") from = date; else to = date;
        }
        if (from > to) return Invalid("from must not follow to.");
        var source = request.Query["source"].ToString().Trim();
        var type = request.Query["type"].ToString().Trim();
        if (source.Length > 600 || type.Length > 40) return Invalid("Filter is too long.");
        // The same publication boundary feeds search, totals, facets, detail, and sitemap.
        // Search the bounded, rendered fields only: no note/body/metadata predicates.
        var items = await PublicCatalog.BuildAsync(db, await PublicCatalog.EligibleRecordsAsync(db, ct), ct);
        IEnumerable<PublicItemDto> selected = items;
        if (topicId.HasValue) selected = selected.Where(i => i.Topics.Any(t => t.Id == topicId));
        if (source.Length > 0) selected = selected.Where(i => i.SourceName.Equals(source, StringComparison.OrdinalIgnoreCase));
        if (type.Length > 0) selected = selected.Where(i => i.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        if (from.HasValue) selected = selected.Where(i => i.PublishedAt.HasValue && DateOnly.FromDateTime(i.PublishedAt.Value.UtcDateTime) >= from.Value);
        if (to.HasValue) selected = selected.Where(i => i.PublishedAt.HasValue && DateOnly.FromDateTime(i.PublishedAt.Value.UtcDateTime) <= to.Value);
        if (q.Length > 0) selected = selected.Where(i =>
            string.Join(' ', new[] { i.Title, i.Author, i.Excerpt, i.SourceName, i.Type, i.Provider, i.CanonicalUrl }
                .Concat(i.Topics.Select(t => t.Name))).Contains(q, StringComparison.OrdinalIgnoreCase));
        var ordered = selected.OrderByDescending(i => i.IngestedAt).ThenBy(i => i.Id).ToList();
        return Results.Ok(new PublicCatalogPage(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ordered.Count, page, pageSize));
    }

    private static async Task<IResult> Detail(Guid id, HttpResponse response, AppDbContext db, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var records = await PublicCatalog.EligibleRecordsAsync(db, ct);
        var item = await PublicCatalog.ResolveAsync(db, id, records, ct);
        return item == null ? Results.NotFound() : Results.Ok((await PublicCatalog.BuildAsync(db, [item], ct))[0]);
    }

    private static async Task<IResult> Related(Guid id, HttpResponse response, AppDbContext db, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var records = await PublicCatalog.EligibleRecordsAsync(db, ct);
        var item = await PublicCatalog.ResolveAsync(db, id, records, ct);
        if (item == null) return Results.NotFound();
        var dtos = await PublicCatalog.BuildAsync(db, records, ct);
        var origin = dtos.Single(i => i.Id == item.Id);
        // Fixed metadata scoring, independent of preferences, reading history, and AI.
        var topics = origin.Topics.Select(t => t.Id).ToHashSet();
        var related = dtos.Where(i => i.Id != item.Id)
            .Select(i => new
            {
                Item = i,
                Score = i.Topics.Count(t => topics.Contains(t.Id)) * 4 +
                (i.SourceName == origin.SourceName ? 1 : 0) + (i.Type == origin.Type ? 1 : 0)
            })
            .Where(i => i.Score > 0).OrderByDescending(i => i.Score)
            .ThenByDescending(i => i.Item.IngestedAt).ThenBy(i => i.Item.Id).Take(12).Select(i => i.Item).ToList();
        return Results.Ok(related);
    }

    private static async Task<IResult> Filters(HttpResponse response, AppDbContext db, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var items = await PublicCatalog.BuildAsync(db, await PublicCatalog.EligibleRecordsAsync(db, ct), ct);
        return Results.Ok(new
        {
            topics = items.SelectMany(i => i.Topics).DistinctBy(t => t.Id).OrderBy(t => t.Name),
            sources = items.Select(i => i.SourceName).Distinct().Order(StringComparer.Ordinal),
            types = items.Select(i => i.Type).Distinct().Order(StringComparer.Ordinal)
        });
    }

    private static async Task<IResult> Sitemap(HttpResponse response, AppDbContext db, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var records = await PublicCatalog.EligibleRecordsAsync(db, ct);
        return Results.Ok(records.OrderBy(i => i.Id).Select(i => new { i.Id, i.UpdatedAt }));
    }
}
