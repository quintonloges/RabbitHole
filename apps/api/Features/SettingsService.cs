using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;

namespace RabbitHole.Features;

public sealed class UserSettings
{
    public int EditionSize { get; set; } = 25;
    public double Serendipity { get; set; } = 0.15;
    public double Freshness { get; set; } = 0.5;
    public bool PracticeEnabled { get; set; } = false;
    public string Theme { get; set; } = "dark";
    public bool AiEnabled { get; set; }
    public bool IngestionPaused { get; set; }
}

public static class SettingsService
{
    public static async Task<bool> IsIngestionPausedAsync(AppDbContext db, IConfiguration config, CancellationToken ct) =>
        config.GetValue<bool>("Ingestion:Paused") || (await ReadAsync(db, ct)).IngestionPaused;

    public static async Task<UserSettings> ReadAsync(AppDbContext db, CancellationToken ct)
    {
        var value = await db.Settings.AsNoTracking().Where(s => s.Key == "user").Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (value == null) return new();
        try { return JsonSerializer.Deserialize<UserSettings>(value) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static async Task SaveAsync(AppDbContext db, UserSettings value, CancellationToken ct)
    {
        var row = await db.Settings.FindAsync(["user"], ct);
        if (row == null) db.Settings.Add(new AppSetting { Key = "user", Value = JsonSerializer.Serialize(value) });
        else row.Value = JsonSerializer.Serialize(value);
        await db.SaveChangesAsync(ct);
    }
}
