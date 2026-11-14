using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;

namespace RabbitHole.Ingestion;

/// <summary>Runs an independent, bounded daily archive batch without changing fresh-feed health.</summary>
public sealed class SourceBackfillService(AppDbContext db, SourceBackfill backfill, IngestionService ingestion,
    IConfiguration config, ILogger<SourceBackfillService> logger)
{
    public async Task<IngestionJob> RunAsync(Source source, CancellationToken ct, bool force = false, bool checkOnly = false)
    {
        using var sourceLock = await IngestionService.AcquireSourceLockAsync(source.Id, ct);
        if (db.Entry(source).State == EntityState.Detached) db.Attach(source);
        await db.Entry(source).ReloadAsync(ct);
        if (db.Entry(source).State == EntityState.Detached) throw new KeyNotFoundException("Source no longer exists.");
        var now = DateTimeOffset.UtcNow;
        var skip = !source.Enabled ? "Source is disabled." : !source.BackfillEnabled ? "Archive backfill is disabled." :
            await SettingsService.IsIngestionPausedAsync(db, config, ct) ? "Ingestion is paused." :
            !force && source.BackfillStatus == "scan-limit" ? "Automatic archive checks are paused at the scan limit. A manual check can retry." :
            !force && source.BackfillNextAt > now ? "The next daily archive batch is not due yet." : null;
        if (skip is not null)
            return new IngestionJob { SourceId = source.Id, Kind = checkOnly ? "backfill-check" : "backfill", Status = "skipped", Error = skip, FinishedAt = now };

        var wasFailing = source.BackfillFailureCount > 0 || source.BackfillStatus is "failed" or "interrupted";
        var job = new IngestionJob { SourceId = source.Id, Kind = checkOnly ? "backfill-check" : "backfill" };
        db.Jobs.Add(job);
        source.BackfillLastAttemptAt = now;
        source.BackfillStatus = "running";
        source.BackfillMessage = null;
        await db.SaveChangesAsync(ct);
        var imported = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var batch = checkOnly ? await backfill.CheckYoutubeAsync(source, deadline.Token) : await backfill.FetchAsync(source, deadline.Token);
            foreach (var item in (checkOnly ? [] : batch.Items).Take(Math.Clamp(source.BackfillLimit, 1, 10)))
            {
                var (_, created) = await ingestion.UpsertAsync(item, source.Id, source.TopicIds, deadline.Token);
                if (created) imported++;
            }
            deadline.Token.ThrowIfCancellationRequested();
            // Advance only once every candidate was durably processed. Retries deduplicate partial imports.
            if (!checkOnly) source.BackfillStateJson = batch.StateJson;
            if (batch.Status is not ("needs-key" or "unsupported" or "unavailable")) source.BackfillLastSuccessAt = DateTimeOffset.UtcNow;
            if (!checkOnly) source.BackfillNextAt = DateTimeOffset.UtcNow.AddDays(1);
            else if (wasFailing) source.BackfillNextAt = DateTimeOffset.UtcNow; // Successful diagnostics resume a backed-off source.
            source.BackfillStatus = batch.Status;
            source.BackfillMessage = batch.Message;
            source.BackfillFailureCount = 0;
            source.BackfillImportedCount += imported;
            job.Imported = imported;
            job.Status = "completed";
            job.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(deadline.Token);
        }
        catch (Exception error)
        {
            logger.LogWarning("Source {SourceId} archive backfill failed: {Type}", source.Id, error.GetType().Name);
            // Discard failed upsert mutations and any uncommitted cursor before recording health.
            db.ChangeTracker.Clear();
            db.Attach(source);
            await db.Entry(source).ReloadAsync(CancellationToken.None);
            db.Attach(job);
            await db.Entry(job).ReloadAsync(CancellationToken.None);
            var interrupted = error is OperationCanceledException && ct.IsCancellationRequested;
            var scanLimit = error is YtDlpException { Code: "request-limit" };
            source.BackfillFailureCount = Math.Min(source.BackfillFailureCount + 1, 12);
            source.BackfillNextAt = DateTimeOffset.UtcNow.AddDays(Math.Min(7, Math.Pow(2, Math.Min(source.BackfillFailureCount - 1, 3))));
            if (scanLimit) source.BackfillNextAt = null;
            source.BackfillStatus = scanLimit ? "scan-limit" : interrupted ? "interrupted" : "failed";
            source.BackfillMessage = interrupted ? "Archive backfill interrupted; it can be retried." : SafeError(error);
            source.BackfillImportedCount += imported;
            job.Imported = imported;
            job.Status = interrupted ? "interrupted" : "failed";
            job.Error = source.BackfillMessage;
            job.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            if (interrupted) throw;
        }
        return job;
    }

    private string SafeError(Exception error)
    {
        var message = error is YtDlpException ? error.Message : IngestionService.PublicError(error);
        foreach (var setting in new[] { "OpenAlex:ApiKey", "OPENALEX_API_KEY", "YouTube:ApiKey", "YOUTUBE_API_KEY" })
            if (config[setting] is { Length: > 0 } secret)
            {
                message = message.Replace(secret, "[redacted]", StringComparison.Ordinal);
                message = message.Replace(Uri.EscapeDataString(secret), "[redacted]", StringComparison.OrdinalIgnoreCase);
            }
        return message;
    }
}
