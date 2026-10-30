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
        var selected = PublicCatalog.Visible(db);
        if (topicId.HasValue) selected = selected.Where(i => i.Topics.Any(t => t.TopicId == topicId));
        if (source.Length > 0) { var key = source.ToUpperInvariant(); selected = selected.Where(i => i.SourceKey == key); }
        if (type.Length > 0) { var key = type.ToUpperInvariant(); selected = selected.Where(i => i.TypeKey == key); }
        if (from.HasValue) { var day = from.Value.DayNumber; selected = selected.Where(i => i.PublishedDay >= day); }
        if (to.HasValue) { var day = to.Value.DayNumber; selected = selected.Where(i => i.PublishedDay <= day); }
        if (q.Length > 0) { var term = q.ToUpperInvariant(); selected = selected.Where(i => i.SearchText.Contains(term)); }
        var total = await selected.CountAsync(ct);
        var records = await selected.OrderByDescending(i => i.IngestedAt).ThenBy(i => i.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Results.Ok(new PublicCatalogPage(await PublicCatalog.BuildAsync(db, records, ct), total, page, pageSize));
    }

    private static async Task<IResult> Detail(Guid id, HttpResponse response, AppDbContext db, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var item = await PublicCatalog.ResolveAsync(db, id, ct);
        return item == null ? Results.NotFound() : Results.Ok((await PublicCatalog.BuildAsync(db, [item], ct))[0]);
    }

    private static async Task<IResult> Related(Guid id, HttpResponse response, AppDbContext db, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var item = await PublicCatalog.ResolveAsync(db, id, ct);
        if (item == null) return Results.NotFound();
        // Fixed metadata scoring, independent of preferences, reading history, and AI.
        var topics = await db.PublicCatalogTopics.Where(t => t.ItemId == item.Id).Select(t => t.TopicId).ToListAsync(ct);
        var related = await PublicCatalog.Visible(db).Where(i => i.Id != item.Id)
            .Select(i => new
            {
                Item = i,
                Score = i.Topics.Count(t => topics.Contains(t.TopicId)) * 4 +
                (i.SourceName == item.SourceName ? 1 : 0) + (i.Type == item.Type ? 1 : 0)
            })
            .Where(i => i.Score > 0).OrderByDescending(i => i.Score)
            .ThenByDescending(i => i.Item.IngestedAt).ThenBy(i => i.Item.Id).Take(12).Select(i => i.Item).ToListAsync(ct);
        return Results.Ok(await PublicCatalog.BuildAsync(db, related, ct));
    }

    private static async Task<IResult> Filters(HttpResponse response, AppDbContext db, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        var items = PublicCatalog.Visible(db);
        return Results.Ok(new
        {
            topics = await items.SelectMany(i => i.Topics).Select(t => new { Id = t.TopicId, t.Name }).Distinct().OrderBy(t => t.Name).ToListAsync(ct),
            sources = await items.Select(i => i.SourceName).Distinct().OrderBy(s => s).ToListAsync(ct),
            types = await items.Select(i => i.Type).Distinct().OrderBy(t => t).ToListAsync(ct)
        });
    }

    private static async Task<IResult> Sitemap(HttpResponse response, AppDbContext db, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        return Results.Ok(await PublicCatalog.Visible(db).OrderBy(i => i.Id).Select(i => new { i.Id, i.UpdatedAt }).ToListAsync(ct));
    }
}
