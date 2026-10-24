using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;

if (args is ["--parse-document", var input, var kind, var output])
{
    await RabbitHole.Features.DocumentParser.RunWorkerAsync(input, kind, output);
    return;
}

if (args.Any(a => a.StartsWith("--admin-password", StringComparison.Ordinal)) && args is not ["--admin-password"])
{
    Console.Error.WriteLine("Run --admin-password alone in an interactive terminal. Do not pass passwords as command arguments.");
    Environment.ExitCode = 1;
    return;
}
var provisionAdmin = args is ["--admin-password"];
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--admin-password").ToArray());
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 52L * 1024 * 1024);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter()));
var connection = builder.Configuration.GetConnectionString("Default") ?? builder.Configuration["DATABASE_URL"] ?? "Host=localhost;Port=5432;Database=rabbithole;Username=rabbithole;Password=rabbithole";
if (builder.Configuration["DATABASE_PROVIDER"]?.Equals("sqlite", StringComparison.OrdinalIgnoreCase) == true)
    builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
else builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<Ranking>();
builder.Services.AddIngestion();
builder.Services.AddDocuments();
builder.Services.AddAi();
builder.AddApiSecurity();

var app = builder.Build();
app.UseExceptionHandler();
app.UseApiSecurity();
app.MapOpenApi();
app.MapGet("/api/healthz", async (AppDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Problem(title: "Database unavailable", statusCode: 503)).AllowAnonymous();
app.MapAdminAuthEndpoints();
app.MapPublicCatalogEndpoints();
app.MapCoreEndpoints();
app.MapIngestionEndpoints();
app.MapDocumentEndpoints();
app.MapAiEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsNpgsql()) await db.Database.MigrateAsync();
    else
    {
        await db.Database.EnsureCreatedAsync();
        await SqliteCatalogSchema.ApplyAsync(db);
        await SqliteSecuritySchema.ApplyAsync(db);
    }
    if (!await db.Topics.AnyAsync())
    {
        db.Topics.AddRange(
            new Topic { Name = "Frontier AI", Description = "Models, robotics, agents, and researchers' devblogs", Keywords = "artificial intelligence, robotics, agents, model research", Weight = 5 },
            new Topic { Name = "Consciousness & philosophy", Description = "Mind, qualia, moral status, and philosophy of science", Keywords = "consciousness, qualia, mind, philosophy", Weight = 5 },
            new Topic { Name = "Speculative history & mysteries", Description = "Myths, lost civilizations, UFO lore, cryptids, and esotericism", Keywords = "flood myths, lost civilizations, UFO, cryptids, occult", Weight = 4 },
            new Topic { Name = "Parapsychology", Description = "Research, historical accounts, and experiments", Keywords = "psi, parapsychology, anomalous experience", Weight = 3 },
            new Topic { Name = "Science & psychology", Description = "Surprising research across disciplines", Keywords = "science, psychology, neuroscience, anthropology", Weight = 4 },
            new Topic { Name = "Software engineering", Description = "Architecture, systems, and development practice", Keywords = "software engineering, systems design, architecture", Weight = 4 },
            new Topic { Name = "Music discovery", Description = "Production, DJ technique, and OG/minimal/wonky/hypnotic 140 BPM riddim and dubstep; taste seeds include AD, 50 Carrot, Spass, Ayonikz, early INFEKT, and Subfiltronik", Keywords = "music production, DJ techniques, 140 BPM, minimal riddim, wonky riddim, hypnotic dubstep, AD riddim, 50 Carrot, Spass, Ayonikz, early INFEKT, Subfiltronik", Weight = 3 },
            new Topic { Name = "Unexpected finds", Description = "Cross-disciplinary detours", Keywords = "curiosity, unexpected", Weight = 2, CuriosityOnly = true });
        await db.SaveChangesAsync();
    }
    await PublicationUpgrade.ApplyAsync(db);
    await CatalogUpgrade.ApplyAsync(db);
    await RecommendationUpgrade.ApplyAsync(db);
}

if (provisionAdmin)
{
    await AdminAuthentication.ProvisionInteractivelyAsync(app.Services);
    return;
}
app.Run();

public partial class Program;
