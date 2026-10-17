using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Ingestion;

namespace RabbitHole.Features;

public sealed record SourceInput(string? Name, string? Kind, string? Url, string? Query,
    List<Guid>? TopicIds, int? IntervalMinutes, int? ResultLimit, bool? Enabled);
public sealed record UrlImportInput(string Url, string? Title);
public sealed record ResearchImportInput(string Provider, string ProviderId);

public static class IngestionEndpoints
{
    public static IServiceCollection AddIngestion(this IServiceCollection services)
    {
        services.AddSingleton<IDnsLookup, SystemDnsLookup>();
        services.AddSingleton<IRemoteFetcher, SafeRemoteFetcher>();
        services.AddScoped<IngestionService>();
        services.AddHostedService<IngestionWorker>();
        return services;
    }

    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");
        group.MapGet("/sources", async (AppDbContext db, CancellationToken ct) =>
            await db.Sources.OrderBy(x => x.Name).ToListAsync(ct));
        group.MapGet("/sources/presets", () => IngestionService.Presets);
        group.MapPost("/sources", async (SourceInput input, AppDbContext db, CancellationToken ct) =>
        {
            var error = Validate(input, null);
            if (error is not null) return Results.Problem(error, statusCode: 400);
            var source = new Source();
            Apply(source, input);
            if (source.TopicIds.Count > 0 && await db.Topics.CountAsync(x => source.TopicIds.Contains(x.Id), ct) != source.TopicIds.Count)
                return Results.Problem("One or more topics do not exist.", statusCode: 400);
            db.Sources.Add(source);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/sources/{source.Id}", source);
        });
        group.MapPatch("/sources/{id:guid}", async (Guid id, SourceInput input, AppDbContext db, CancellationToken ct) =>
        {
            var source = await db.Sources.FindAsync([id], ct);
            if (source is null) return Results.NotFound();
            var error = Validate(input, source);
            if (error is not null) return Results.Problem(error, statusCode: 400);
            Apply(source, input);
            if (source.TopicIds.Count > 0 && await db.Topics.CountAsync(x => source.TopicIds.Contains(x.Id), ct) != source.TopicIds.Count)
                return Results.Problem("One or more topics do not exist.", statusCode: 400);
            source.NextSyncAt = DateTimeOffset.UtcNow;
            source.ETag = null;
            source.LastModified = null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(source);
        });
        group.MapDelete("/sources/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var source = await db.Sources.FindAsync([id], ct);
            if (source is null) return Results.NotFound();
            // Retain imported items and user state when unsubscribing.
            var items = await db.Items.Where(x => x.SourceId == id).ToListAsync(ct);
            foreach (var item in items) item.SourceId = null;
            var jobs = await db.Jobs.Where(x => x.SourceId == id).ToListAsync(ct);
            db.Jobs.RemoveRange(jobs);
            db.Sources.Remove(source);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapPost("/sources/{id:guid}/test", async (Guid id, AppDbContext db, IngestionService ingestion, CancellationToken ct) =>
        {
            var source = await db.Sources.FindAsync([id], ct);
            if (source is null) return Results.NotFound();
            try
            {
                var batch = await ingestion.FetchSourceAsync(source, ct, conditional: false);
                return Results.Ok(new
                {
                    ok = true,
                    count = batch.Items.Count,
                    sample = batch.Items.Take(3),
                    discoveredFeedUrl = batch.DiscoveredFeedUrl,
                    notModified = batch.NotModified
                });
            }
            catch (Exception e) when (e is not OperationCanceledException)
            { return Results.Problem(IngestionService.PublicError(e), statusCode: 502); }
        });
        group.MapPost("/sources/{id:guid}/refresh", async (Guid id, AppDbContext db, IngestionService ingestion, CancellationToken ct) =>
        {
            var source = await db.Sources.FindAsync([id], ct);
            if (source is null) return Results.NotFound();
            return Results.Ok(await ingestion.RefreshAsync(source, ct));
        });
        group.MapGet("/jobs", async (AppDbContext db, CancellationToken ct) =>
            (await db.Jobs.ToListAsync(ct)).OrderByDescending(x => x.StartedAt).Take(100).ToArray());
        group.MapPost("/items/import-url", async (UrlImportInput input, IngestionService ingestion, CancellationToken ct) =>
        {
            try { return Results.Ok(await ingestion.ImportUrlAsync(input.Url, input.Title, ct)); }
            catch (RemoteFetchException e) { return Results.Problem(e.Message, statusCode: 400); }
        });
        group.MapGet("/research/search", async (string provider, string q, IngestionService ingestion, CancellationToken ct) =>
        {
            try { return Results.Ok(new { items = await ingestion.SearchResearchAsync(provider, q, ct) }); }
            catch (ArgumentException e) { return Results.Problem(e.Message, statusCode: 400); }
            catch (Exception e) when (e is not OperationCanceledException)
            { return Results.Problem(IngestionService.PublicError(e), statusCode: 502); }
        });
        group.MapPost("/research/import", async (ResearchImportInput input, IngestionService ingestion, CancellationToken ct) =>
        {
            try { return Results.Ok(await ingestion.ImportResearchAsync(input.Provider, input.ProviderId, ct)); }
            catch (ArgumentException e) { return Results.Problem(e.Message, statusCode: 400); }
            catch (KeyNotFoundException e) { return Results.Problem(e.Message, statusCode: 404); }
            catch (Exception e) when (e is not OperationCanceledException)
            { return Results.Problem(IngestionService.PublicError(e), statusCode: 502); }
        });
        return app;
    }

    private static string? Validate(SourceInput input, Source? existing)
    {
        var name = input.Name ?? existing?.Name;
        var kind = input.Kind ?? existing?.Kind;
        var url = input.Url ?? existing?.Url;
        var query = input.Query ?? existing?.Query;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) return "Source name must be 1–120 characters.";
        if (input.IntervalMinutes is < 15 or > 10080 || input.ResultLimit is < 1 or > 100)
            return "Interval must be 15–10080 minutes and result limit 1–100.";
        if ((input.TopicIds?.Count ?? 0) > 20) return "Too many topics selected.";
        if (kind is "rss" or "youtube")
        {
            try
            {
                var parsedUrl = SafeRemoteFetcher.ValidateUrl(url ?? "");
                if (kind == "youtube" && ProviderAdapters.YoutubeChannelId(parsedUrl.ToString()) is null)
                    return "Use a YouTube /channel/UC… URL for channel feeds.";
            }
            catch (RemoteFetchException e) { return e.Message; }
        }
        else if (kind is "openalex" or "arxiv")
        {
            if (query is null || query.Trim().Length < 2 || query.Trim().Length > 200)
                return "Research source needs a 2–200 character query.";
        }
        else return "Supported source kinds are rss, openalex, arxiv, and youtube.";
        return null;
    }

    private static void Apply(Source source, SourceInput input)
    {
        if (input.Name is not null) source.Name = input.Name.Trim();
        if (input.Kind is not null) source.Kind = input.Kind;
        if (input.Url is not null) source.Url = input.Url.Trim();
        if (input.Query is not null) source.Query = input.Query.Trim();
        if (input.TopicIds is not null) source.TopicIds = input.TopicIds.Distinct().ToList();
        source.IntervalMinutes = input.IntervalMinutes ?? source.IntervalMinutes;
        source.ResultLimit = input.ResultLimit ?? source.ResultLimit;
        source.Enabled = input.Enabled ?? source.Enabled;
    }
}

public sealed class IngestionWorker(IServiceScopeFactory scopes, ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var ingestion = scope.ServiceProvider.GetRequiredService<IngestionService>();
                    var stale = (await db.Jobs.Where(x => x.Status == "running").ToListAsync(stoppingToken))
                        .Where(x => x.StartedAt < DateTimeOffset.UtcNow.AddMinutes(-2)).Take(20).ToList();
                    foreach (var job in stale)
                    {
                        job.Status = "interrupted";
                        job.Error = "The previous refresh stopped before completion; retry is available.";
                        job.FinishedAt = DateTimeOffset.UtcNow;
                    }
                    if (stale.Count > 0) await db.SaveChangesAsync(stoppingToken);
                    var due = (await db.Sources.Where(x => x.Enabled).ToListAsync(stoppingToken))
                        .Where(x => x.NextSyncAt <= DateTimeOffset.UtcNow)
                        .OrderBy(x => x.NextSyncAt).Take(3).ToList();
                    foreach (var source in due) await ingestion.RefreshAsync(source, stoppingToken);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { logger.LogWarning(error, "Ingestion worker cycle failed"); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
