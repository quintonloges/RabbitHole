using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;

// Offline, no ingestion/AI. The input export stays private. Seed IDs come from the
// captured baseline, not production ranking. Fix the clock as well as the catalog.
if (args.Length != 3) throw new ArgumentException("snapshot.json baseline-audit.json output.json");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(args[0]));
using var baseline = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
var root = snapshot.RootElement;
AuditClock.Now = root.GetProperty("exportedAt").GetDateTimeOffset();
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
void Load<T>(string key) where T : class => db.Set<T>().AddRange(root.GetProperty(key).Deserialize<List<T>>(json)!);
Load<Topic>("topics"); Load<Source>("sources"); Load<LibraryDocument>("documents"); Load<ContentItem>("items");
Load<ItemState>("states"); Load<ItemSource>("itemSources"); Load<RabbitHole.Domain.RabbitHole>("holes"); Load<RabbitNode>("nodes");
foreach (var hole in db.Holes.Local) if (!root.GetProperty("holes").EnumerateArray().First(h => h.GetProperty("id").GetGuid() == hole.Id).TryGetProperty("origin", out _)) hole.Origin = "legacy-unknown";
foreach (var node in db.Nodes.Local) if (!root.GetProperty("nodes").EnumerateArray().First(n => n.GetProperty("id").GetGuid() == node.Id).TryGetProperty("origin", out _)) node.Origin = "legacy-unknown";
await SettingsService.SaveAsync(db, root.GetProperty("settings").Deserialize<UserSettings>(json)!, default);
await RecommendationUpgrade.ApplyAsync(db);
var items = await db.Items.ToDictionaryAsync(i => i.Id);
#if LEGACY_RANKING
var ranker = new LegacyRanking(db);
async Task<List<Recommendation>> Related(ContentItem item, HashSet<Guid>? visited = null) => (await ranker.RelatedAsync(item, 20, default))
    .Select(r => new Recommendation(r.Item, new() { Kind = "legacy", Summary = r.Reason, Evidence = r.Evidence.Select(id => new RecommendationEvidence(id, items[id].Title, "legacy-unspecified")).ToList() })).ToList();
var daily = (await ranker.SelectAsync(root.GetProperty("settings").GetProperty("editionSize").GetInt32(), AuditClock.Now.ToString("yyyy-MM-dd"), null, default))
    .Select(r => new Recommendation(r.Item, new() { Kind = "legacy", Summary = r.Reason, Evidence = r.Evidence.Select(id => new RecommendationEvidence(id, items[id].Title, "legacy-unspecified")).ToList() })).ToList();
#else
var ranker = new Ranking(db);
Task<List<Recommendation>> Related(ContentItem item, HashSet<Guid>? visited = null) => ranker.RelatedAsync(item, 20, default, visited);
var daily = await ranker.SelectAsync(root.GetProperty("settings").GetProperty("editionSize").GetInt32(), AuditClock.Now.ToString("yyyy-MM-dd"), null, default, AuditClock.Now);
#endif
object Row(Recommendation r) => new { id = r.Item.Id, r.Item.Title, r.Reason, evidenceItemIds = r.Evidence, r.Rationale, concepts = Concepts.Match(r.Item), r.Revisit, r.Item.SourceId, r.Item.CanonicalUrl };
var allRelated = new Dictionary<Guid, object[]>();
foreach (var item in items.Values) allRelated[item.Id] = (await Related(item)).Select(Row).ToArray();
var paths = new List<object>();
foreach (var path in baseline.RootElement.GetProperty("paths").EnumerateArray())
{
    var current = items[path.GetProperty("levels")[0].GetProperty("seedId").GetGuid()];
    var visited = new HashSet<Guid> { current.Id };
    var levels = new List<object>();
    for (var level = 1; level <= 3; level++)
    {
        var candidates = await Related(current, visited);
        var next = candidates.FirstOrDefault(r => !visited.Contains(r.Item.Id));
        levels.Add(new { level, seedId = current.Id, seedTitle = current.Title, visitedIds = visited.ToArray(), candidates = candidates.Select(Row).ToArray(), chosenId = next?.Item.Id });
        if (next is null) break;
        current = next.Item; visited.Add(current.Id);
    }
    paths.Add(new { category = path.GetProperty("category").GetString(), levels });
}
await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(new { catalogCount = items.Count, asOf = AuditClock.Now, paths, related = allRelated, daily = daily.Select(Row) }, json));
Console.WriteLine($"Audited {items.Count} actual records at {AuditClock.Now:O}; output {args[2]}");

public static class AuditClock { public static DateTimeOffset Now { get; set; } }
