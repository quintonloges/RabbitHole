using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using System.Net;

if (args is ["--parse-document", var input, var kind, var output])
{
    await RabbitHole.Features.DocumentParser.RunWorkerAsync(input, kind, output);
    return;
}

var builder = WebApplication.CreateBuilder(args);
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

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    var loopback = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
    if (!loopback && !app.Configuration.GetValue<bool>("RABBITHOLE_ALLOW_REMOTE_HOSTS"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await Results.Problem(title: "Host not allowed", statusCode: 403).ExecuteAsync(context);
        return;
    }
    if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        var origin = context.Request.Headers.Origin.ToString();
        var allowedOrigins = (app.Configuration["RABBITHOLE_ALLOWED_ORIGINS"] ?? app.Configuration["RABBITHOLE_ALLOWED_ORIGIN"] ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var originMatches = string.IsNullOrWhiteSpace(origin) || allowedOrigins.Any(value => value.Equals(origin, StringComparison.OrdinalIgnoreCase)) || (Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && uri.Port == (context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80)));
        if ((fetchSite == "cross-site" && !allowedOrigins.Any(value => value.Equals(origin, StringComparison.OrdinalIgnoreCase))) || !originMatches)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await Results.Problem(title: "Cross-site request blocked", statusCode: 403).ExecuteAsync(context);
            return;
        }
    }
    await next();
});
app.MapOpenApi();
app.MapGet("/api/healthz", async (AppDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Problem(title: "Database unavailable", statusCode: 503));
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
    await CatalogUpgrade.ApplyAsync(db);
}

app.Run();

public partial class Program;
