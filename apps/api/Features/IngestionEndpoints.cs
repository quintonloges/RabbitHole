using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Ingestion;

namespace RabbitHole.Features;

public sealed record SourceInput(string? Name, string? Kind, string? Url, string? Query,
    List<Guid>? TopicIds, int? IntervalMinutes, int? ResultLimit, bool? Enabled,
    bool? BackfillEnabled = null, int? BackfillLimit = null, string? BackfillUrl = null);
public sealed record UrlImportInput(string Url, string? Title, string? FeedUrl = null);
public sealed record ResearchImportInput(string Provider, string ProviderId);

public static class IngestionEndpoints
{
    public static IServiceCollection AddIngestion(this IServiceCollection services)
    {
        services.AddSingleton<IDnsLookup, SystemDnsLookup>();
        services.AddSingleton<IRemoteFetcher, SafeRemoteFetcher>();
        services.AddScoped<IngestionService>();
        services.AddSingleton<IYtDlpClient, YtDlpClient>();
        services.AddScoped<SourceBackfill>();
        services.AddScoped<SourceBackfillService>();
        services.AddHostedService<IngestionWorker>();
        return services;
    }

    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");
        group.AddEndpointFilter(async (context, next) =>
        {
            var path = context.HttpContext.Request.Path.Value ?? "";
            var remoteOperation = path is "/api/items/import-url" or "/api/research/import" or "/api/research/search" ||
                path.StartsWith("/api/sources/", StringComparison.Ordinal) && (path.EndsWith("/refresh") || path.EndsWith("/test") || path.EndsWith("/backfill") || path.EndsWith("/backfill-check"));
            if (remoteOperation && await SettingsService.IsIngestionPausedAsync(
                context.HttpContext.RequestServices.GetRequiredService<AppDbContext>(),
                context.HttpContext.RequestServices.GetRequiredService<IConfiguration>(), context.HttpContext.RequestAborted))
                return Results.Problem(title: "Ingestion paused", detail: "Resume ingestion in Settings or the server configuration before contacting providers.", statusCode: 409);
            return await next(context);
        });
        group.MapGet("/sources", async (AppDbContext db, CancellationToken ct) =>
            await db.Sources.OrderBy(x => x.Name).ToListAsync(ct));
        group.MapGet("/sources/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var source = await db.Sources.AsNoTracking().Where(s => s.Id == id).Select(s => new { s.Id, s.Name }).SingleOrDefaultAsync(ct)
                ?? await db.ItemSources.AsNoTracking().Where(s => s.SourceKey == id).OrderByDescending(s => s.SourceVersion)
                    .ThenBy(s => s.Name).Select(s => new { Id = s.SourceKey, s.Name }).FirstOrDefaultAsync(ct);
            return source is null ? Results.Problem(title: "Source not found", detail: "This source is no longer in your library.", statusCode: 404) : Results.Ok(source);
        });
        group.MapGet("/sources/presets", () => SourcePresets.All);
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
            using var sourceLock = await IngestionService.AcquireSourceLockAsync(id, ct);
            var source = await db.Sources.FindAsync([id], ct);
            if (source is null) return Results.NotFound();
            var error = Validate(input, source);
            if (error is not null) return Results.Problem(error, statusCode: 400);
            var resetWatch = (input.Kind is not null && input.Kind != source.Kind) ||
                (input.Query is not null && input.Query.Trim() != source.Query) ||
                (input.ResultLimit is not null && input.ResultLimit != source.ResultLimit);
            var replaceOrigin = (input.Kind is not null && input.Kind != source.Kind) ||
                (input.Url is not null && input.Url.Trim() != source.Url) ||
                (input.Query is not null && input.Query.Trim() != source.Query);
            if (replaceOrigin)
            {
                // Preserve prior origin snapshots and feed-local IDs when a subscription changes identity.
                foreach (var item in await db.Items.IgnoreQueryFilters().Where(x => x.SourceId == id).ToListAsync(ct))
                { if (item.MergedIntoId is null) await ItemCatalog.AttachSourceAsync(db, item, id, ct); item.SourceId = null; }
                foreach (var link in await db.ItemSources.Where(x => x.SourceId == id).ToListAsync(ct)) link.SourceId = null;
                foreach (var link in db.ItemSources.Local.Where(x => x.SourceId == id)) link.SourceId = null;
                source.IdentityVersion = checked(source.IdentityVersion + 1);
            }
            var resetBackfill = replaceOrigin || input.BackfillUrl is not null && input.BackfillUrl.Trim() != source.BackfillUrl;
            Apply(source, input);
            if (resetBackfill)
            {
                source.BackfillStateJson = null;
                source.BackfillNextAt = null;
                source.BackfillStatus = "pending";
                source.BackfillMessage = null;
                source.BackfillLastAttemptAt = null;
                source.BackfillLastSuccessAt = null;
                source.BackfillImportedCount = 0;
                source.BackfillFailureCount = 0;
            }
            if (source.TopicIds.Count > 0 && await db.Topics.CountAsync(x => source.TopicIds.Contains(x.Id), ct) != source.TopicIds.Count)
                return Results.Problem("One or more topics do not exist.", statusCode: 400);
            source.NextSyncAt = DateTimeOffset.UtcNow;
            source.ETag = null;
            source.LastModified = null;
            if (resetWatch || replaceOrigin)
            {
                source.ResearchCursor = null; source.ResearchFromDate = null;
                source.ResearchWindowStartedAt = null; source.ResearchCheckpointAt = null;
            }
            var updateItemTopics = replaceOrigin || input.TopicIds is not null;
            if (updateItemTopics || input.Name is not null)
            {
                var links = await db.ItemSources.Where(x => x.SourceId == id).ToListAsync(ct);
                foreach (var link in links.Where(x => x.SourceId == id))
                {
                    link.Name = source.Name;
                    link.Kind = source.Kind;
                    link.Url = source.Url;
                    link.TopicIds = source.TopicIds.ToList();
                }
            }
            await db.SaveChangesAsync(ct);
            if (updateItemTopics) await TopicClassification.ReclassifyAsync(db, ct);
            return Results.Ok(source);
        });
        group.MapDelete("/sources/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            using var sourceLock = await IngestionService.AcquireSourceLockAsync(id, ct);
            var source = await db.Sources.FindAsync([id], ct);
            if (source is null) return Results.NotFound();
            // Retain imported items and user state when unsubscribing.
            var items = await db.Items.IgnoreQueryFilters().Where(x => x.SourceId == id).ToListAsync(ct);
            foreach (var item in items.Where(x => x.MergedIntoId == null)) await ItemCatalog.AttachSourceAsync(db, item, id, ct);
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
        group.MapPost("/sources/{id:guid}/backfill", async (Guid id, AppDbContext db, SourceBackfillService backfill, CancellationToken ct) =>
        {
            var source = await db.Sources.FindAsync([id], ct);
            if (source is null) return Results.NotFound();
            return Results.Ok(await backfill.RunAsync(source, ct, force: true));
        });
        group.MapPost("/sources/{id:guid}/backfill-check", async (Guid id, AppDbContext db, SourceBackfillService backfill, CancellationToken ct) =>
        {
            var source = await db.Sources.FindAsync([id], ct);
            if (source is null) return Results.NotFound();
            if (source.Kind != "youtube") return Results.Problem("This health check is for YouTube sources.", statusCode: 400);
            return Results.Ok(await backfill.RunAsync(source, ct, force: true, checkOnly: true));
        });
        group.MapGet("/jobs", async (AppDbContext db, CancellationToken ct) =>
            (await db.Jobs.ToListAsync(ct)).OrderByDescending(x => x.StartedAt).Take(100).ToArray());
        group.MapPost("/items/import-url", async (UrlImportInput input, IngestionService ingestion, CancellationToken ct) =>
        {
            try { return Results.Ok(await ingestion.ImportUrlAsync(input.Url, input.Title, ct, input.FeedUrl)); }
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
        if (input.BackfillLimit is < 1 or > 10) return "Archive batch limit must be 1-10 items.";
        var archiveUrl = (input.BackfillUrl ?? existing?.BackfillUrl ?? "").Trim();
        if (kind == "rss" && archiveUrl.Length > 2048) return "Archive URL must be at most 2048 characters.";
        if (kind == "rss" && archiveUrl.Length > 0)
        {
            try
            {
                var archive = SafeRemoteFetcher.ValidateUrl(archiveUrl);
                var origin = SafeRemoteFetcher.ValidateUrl(url ?? "");
                if (!string.Equals(archive.Host, origin.Host, StringComparison.OrdinalIgnoreCase))
                    return "Archive URL must use the same host as the source URL.";
            }
            catch (RemoteFetchException e) { return e.Message; }
        }
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
        else if (kind is "openalex" or "arxiv" or "web-archive")
        {
            if (query is null || query.Trim().Length < 2 || query.Trim().Length > 200)
                return "Search source needs a 2–200 character query.";
            if (kind == "web-archive" && (input.ResultLimit ?? existing?.ResultLimit ?? 25) > 3)
                return "Older web discovery is limited to 1–3 original links per refresh.";
            if (kind == "web-archive" && (input.IntervalMinutes ?? existing?.IntervalMinutes ?? 360) < 1440)
                return "Older web discovery refreshes at most daily (1440 minutes).";
        }
        else return "Supported source kinds are rss, openalex, arxiv, youtube, and web-archive.";
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
        source.BackfillEnabled = input.BackfillEnabled ?? source.BackfillEnabled;
        source.BackfillLimit = input.BackfillLimit ?? source.BackfillLimit;
        if (source.Kind != "rss") source.BackfillUrl = "";
        else if (input.BackfillUrl is not null) source.BackfillUrl = input.BackfillUrl.Trim();
    }
}

public sealed class IngestionWorker(IServiceScopeFactory scopes, ILogger<IngestionWorker> logger, IHostEnvironment environment) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    if (await SettingsService.IsIngestionPausedAsync(db, scope.ServiceProvider.GetRequiredService<IConfiguration>(), stoppingToken)) continue;
                    var stale = (await db.Jobs.Where(x => x.Status == "running").ToListAsync(stoppingToken))
                        .Where(x => x.StartedAt < DateTimeOffset.UtcNow.AddMinutes(-2)).Take(20).ToList();
                    foreach (var job in stale)
                    {
                        job.Status = "interrupted";
                        job.Error = "The previous refresh stopped before completion; retry is available.";
                        job.FinishedAt = DateTimeOffset.UtcNow;
                        if (job.Kind is "backfill" or "backfill-check")
                        {
                            job.Error = "The previous archive check stopped before completion; retry is available.";
                            var interruptedSource = await db.Sources.FindAsync([job.SourceId], stoppingToken);
                            if (interruptedSource is { BackfillStatus: "running" } && interruptedSource.BackfillLastAttemptAt <= job.StartedAt)
                            {
                                interruptedSource.BackfillStatus = "interrupted";
                                interruptedSource.BackfillMessage = job.Error;
                                interruptedSource.BackfillFailureCount = Math.Min(interruptedSource.BackfillFailureCount + 1, 12);
                                interruptedSource.BackfillNextAt = DateTimeOffset.UtcNow.AddDays(1);
                            }
                        }
                    }
                    if (stale.Count > 0) await db.SaveChangesAsync(stoppingToken);
                    var due = (await db.Sources.Where(x => x.Enabled).ToListAsync(stoppingToken))
                        .Where(x => x.NextSyncAt <= DateTimeOffset.UtcNow)
                        .OrderBy(x => x.NextSyncAt).Take(3).ToList();
                    foreach (var dueSource in due)
                    {
                        // Give each provider its own unit of work, including after a database error.
                        using var sourceScope = scopes.CreateScope();
                        var sourceDb = sourceScope.ServiceProvider.GetRequiredService<AppDbContext>();
                        if (await SettingsService.IsIngestionPausedAsync(sourceDb, sourceScope.ServiceProvider.GetRequiredService<IConfiguration>(), stoppingToken)) break;
                        var source = await sourceDb.Sources.FindAsync([dueSource.Id], stoppingToken);
                        if (source is null || !source.Enabled) continue;
                        try { await sourceScope.ServiceProvider.GetRequiredService<IngestionService>().RefreshAsync(source, stoppingToken); }
                        catch (Exception error) when (error is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                        { logger.LogWarning("Source {SourceId} could not record refresh status: {Type}", source.Id, error.GetType().Name); }
                    }
                    var archiveDue = (await db.Sources.AsNoTracking().Where(x => x.Enabled && x.BackfillEnabled && x.Kind != "web-archive" && x.BackfillStatus != "scan-limit").ToListAsync(stoppingToken))
                        .Where(x => x.BackfillNextAt is null || x.BackfillNextAt <= DateTimeOffset.UtcNow)
                        .OrderBy(x => x.BackfillNextAt).Take(3).ToList();
                    foreach (var dueSource in archiveDue)
                    {
                        using var sourceScope = scopes.CreateScope();
                        var sourceDb = sourceScope.ServiceProvider.GetRequiredService<AppDbContext>();
                        if (await SettingsService.IsIngestionPausedAsync(sourceDb, sourceScope.ServiceProvider.GetRequiredService<IConfiguration>(), stoppingToken)) break;
                        var source = await sourceDb.Sources.FindAsync([dueSource.Id], stoppingToken);
                        if (source is null) continue;
                        try { await sourceScope.ServiceProvider.GetRequiredService<SourceBackfillService>().RunAsync(source, stoppingToken); }
                        catch (Exception error) when (error is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                        { logger.LogWarning("Source {SourceId} could not record archive backfill status: {Type}", source.Id, error.GetType().Name); }
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { logger.LogWarning(error, "Ingestion worker cycle failed"); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
