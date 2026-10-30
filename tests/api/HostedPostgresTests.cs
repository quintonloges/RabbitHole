using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public sealed class HostedPostgresFactAttribute : FactAttribute
{
    public HostedPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RABBITHOLE_TEST_POSTGRES")))
            Skip = "Requires RABBITHOLE_TEST_POSTGRES pointing at a disposable rabbithole_recommendation_test* database; run scripts/check-recommendations-postgres.ps1.";
    }
}

/// <summary>Real PostgreSQL migrations and HTTP. Each test owns only a random schema
/// inside the explicitly named disposable verification database, never production.</summary>
public class HostedPostgresTests
{
    private sealed class IsolatedPostgres : IAsyncDisposable
    {
        private readonly string original;
        private readonly string schema = "hosted_fixture_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }
        public string KeysPath { get; } = Path.Combine(Path.GetTempPath(), "rabbithole-pg-auth-" + Guid.NewGuid().ToString("N"));
        private IsolatedPostgres(string connection)
        {
            var parsed = new NpgsqlConnectionStringBuilder(connection);
            if (parsed.Database?.StartsWith("rabbithole_recommendation_test", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Hosted PostgreSQL tests require a disposable rabbithole_recommendation_test* database.");
            original = parsed.ConnectionString;
            parsed.SearchPath = schema;
            ConnectionString = parsed.ConnectionString;
        }
        public static async Task<IsolatedPostgres> CreateAsync()
        {
            var result = new IsolatedPostgres(Environment.GetEnvironmentVariable("RABBITHOLE_TEST_POSTGRES")!);
            await using var connection = new NpgsqlConnection(result.original); await connection.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE SCHEMA \"" + result.schema + "\"", connection);
            await command.ExecuteNonQueryAsync();
            return result;
        }
        public AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
        public async ValueTask DisposeAsync()
        {
            if (!Regex.IsMatch(schema, "^hosted_fixture_[a-f0-9]{32}$")) throw new InvalidOperationException("Unexpected fixture schema; refusing cleanup.");
            await using var connection = new NpgsqlConnection(original); await connection.OpenAsync();
            await using var command = new NpgsqlCommand("DROP SCHEMA \"" + schema + "\" CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
    private sealed class Factory(IsolatedPostgres fixture) : AuthenticatedApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("DATABASE_PROVIDER", "postgres");
            builder.UseSetting("ConnectionStrings:Default", fixture.ConnectionString);
            builder.UseSetting("Security:DataProtectionPath", fixture.KeysPath);
            builder.UseSetting("Ingestion:Paused", "true");
            builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services.Where(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(IngestionWorker)).ToList()) services.Remove(descriptor);
            });
        }
    }
    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, text);
            using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone();
        }
    }

    [HostedPostgresFact]
    public async Task Legacy_postgres_schema_upgrades_backfill_before_merge_and_preserve_private_records()
    {
        await using var fixture = await IsolatedPostgres.CreateAsync();
        var winner = Guid.NewGuid(); var duplicate = Guid.NewGuid(); var document = Guid.NewGuid();
        var added = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var before = fixture.Open())
        {
            await before.GetService<IMigrator>().MigrateAsync("20261020023750_DocumentSectionHistory");
            foreach (var (id, kind, provider, url, date) in new[]
            {
                (winner, "article", "rss", "https://example.org/legacy-shared", added),
                (duplicate, "article", "rss", "https://example.org/legacy-shared", added.AddDays(1)),
                (document, "book", "local", "local://document/private-book", added.AddDays(2))
            })
                await before.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Items" ("Id","Kind","Title","CanonicalUrl","Provider","TextProvenance","IngestedAt","UpdatedAt","TopicIds","ClassificationOrigin")
                    VALUES ({id},{kind},{"TEST FIXTURE legacy migration"},{url},{provider},{"metadata"},{date},{date},{"[]"},{"manual"})
                    """);
            Assert.DoesNotContain(await before.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("PublicCatalogAndAdmin"));
        }
        // Actual app startup migrates and runs publication backfill before catalog deduplication.
        using var factory = new Factory(fixture); using var anonymous = factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/healthz")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(db.Database.IsNpgsql()); Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("PublicCatalogAndAdmin"));
        var records = await db.Items.IgnoreQueryFilters().OrderBy(i => i.IngestedAt).ToListAsync();
        Assert.Equal(3, records.Count);
        Assert.True(records.Single(i => i.Id == winner).IsPublic);
        Assert.Equal(winner, records.Single(i => i.Id == duplicate).MergedIntoId);
        Assert.False(records.Single(i => i.Id == document).IsPublic);
        Assert.True(await db.Settings.AnyAsync(s => s.Key == PublicationUpgrade.VersionKey));
        Assert.True(await db.Settings.AnyAsync(s => s.Key == CatalogUpgrade.VersionKey));
        var list = await Read(await anonymous.GetAsync("/api/public/items?q=legacy"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(winner, list.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{document}")).StatusCode);
        Assert.Empty(await db.AdminAccounts.ToListAsync()); Assert.Empty(await db.AdminSessions.ToListAsync());
    }

    [HostedPostgresFact]
    public async Task Postgres_index_preserves_visibility_and_topic_edits_across_stale_writers()
    {
        await using var fixture = await IsolatedPostgres.CreateAsync();
        using var factory = new Factory(fixture); using var anonymous = factory.CreateAnonymousClient();
        await Read(await anonymous.GetAsync("/api/healthz"));
        var topic = new Topic { Name = "TEST FIXTURE original index topic" };
        var item = new ContentItem { Title = "TEST FIXTURE stale writer", CanonicalUrl = "https://example.org/stale-writer", IsPublic = true, TopicIds = [topic.Id] };
        await using var editor = fixture.Open(); editor.AddRange(topic, item); await editor.SaveChangesAsync();
        await using (var hiding = fixture.Open())
        {
            var fresh = await hiding.Items.SingleAsync(i => i.Id == item.Id); fresh.IsPublic = false;
            var freshTopic = await hiding.Topics.SingleAsync(t => t.Id == topic.Id); freshTopic.Name = "TEST FIXTURE renamed index topic";
            await hiding.SaveChangesAsync();
        }
        item.Title = "TEST FIXTURE edited title"; topic.Weight = 1; await editor.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{item.Id}")).StatusCode);
        await using var check = fixture.Open();
        var projection = await check.PublicCatalogEntries.Include(i => i.Topics).SingleAsync(i => i.Id == item.Id);
        Assert.False(projection.IsPublic); Assert.Equal(item.Title, projection.Title);
        Assert.Equal("TEST FIXTURE renamed index topic", Assert.Single(projection.Topics).Name);
        Assert.Empty(await PublicCatalog.Visible(check).ToListAsync());
    }

    [HostedPostgresFact]
    public async Task Postgres_public_filters_and_authentication_publication_state_survive_application_restart()
    {
        await using var fixture = await IsolatedPostgres.CreateAsync();
        var topic = new Topic { Name = "TEST FIXTURE PostgreSQL topic" };
        var source = new Source { Name = "TEST FIXTURE PostgreSQL source", Url = "https://example.org/feed?token=private-subscription", Enabled = false };
        var date = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var hidden = new ContentItem { Title = "TEST FIXTURE hidden PostgreSQL", CanonicalUrl = "https://example.org/pg-hidden", Provider = "rss", IsPublic = true, TopicIds = [topic.Id], SourceId = source.Id, PublishedAt = date, IngestedAt = date };
        var visible = new ContentItem { Title = "TEST FIXTURE visible PostgreSQL", CanonicalUrl = "https://example.org/pg-visible", Provider = "rss", IsPublic = true, TopicIds = [topic.Id], SourceId = source.Id, PublishedAt = date, IngestedAt = date.AddDays(1), Body = "private-body-postgres-sentinel" };
        string cookie;
        using (var original = new Factory(fixture))
        {
            using var client = original.CreateAnonymousClient(); await original.ProvisionAsync();
            using (var scope = original.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.AddRange(topic, source, hidden, visible, new Note { ItemId = visible.Id, Body = "private-note-postgres-sentinel" }, new ItemState { ItemId = visible.Id, Saved = true, Read = true });
                await db.SaveChangesAsync();
            }
            await AuthenticatedApiFactory.RefreshCsrfAsync(client);
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { password = AuthenticatedApiFactory.FixturePassword }); login.EnsureSuccessStatusCode();
            cookie = login.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("RabbitHole.Admin=", StringComparison.Ordinal)).Split(';')[0];
            await AuthenticatedApiFactory.RefreshCsrfAsync(client);
            await Read(await client.PatchAsJsonAsync($"/api/items/{hidden.Id}/publication", new { isPublic = false }));
        }
        using var restarted = new Factory(fixture); using var anonymous = restarted.CreateAnonymousClient(); using var replay = restarted.CreateAnonymousClient();
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/items")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/items/{hidden.Id}")).StatusCode);
        var query = $"q=PostgreSQL&topicId={topic.Id}&source={Uri.EscapeDataString(source.Name)}&type=article&from=2026-01-10&to=2026-01-10&page=1&pageSize=1";
        var page = await Read(await anonymous.GetAsync("/api/public/items?" + query));
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(visible.Id, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        foreach (var term in new[] { "private-body-postgres-sentinel", "private-note-postgres-sentinel" })
            Assert.Equal(0, (await Read(await anonymous.GetAsync("/api/public/items?q=" + term))).GetProperty("total").GetInt32());
        var sitemap = await Read(await anonymous.GetAsync("/api/public/sitemap"));
        Assert.Equal(visible.Id, Assert.Single(sitemap.EnumerateArray()).GetProperty("id").GetGuid());
        await using var check = fixture.Open();
        Assert.False((await check.Items.SingleAsync(i => i.Id == hidden.Id)).IsPublic);
        var state = await check.States.SingleAsync(s => s.ItemId == visible.Id); Assert.True(state.Saved); Assert.True(state.Read); Assert.Null(state.LastOpenedAt);
        var account = await check.AdminAccounts.SingleAsync(); Assert.NotEqual(AuthenticatedApiFactory.FixturePassword, account.PasswordHash);
        var constraint = await Assert.ThrowsAsync<PostgresException>(() => check.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AdminAccounts" ("Id", "PasswordHash", "FailedAttempts", "PasswordChangedAt")
            VALUES (2, 'TEST FIXTURE forbidden second account', 0, now())
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, constraint.SqlState);
        Assert.Equal("CK_AdminAccounts_SingleAccount", constraint.ConstraintName);
        Assert.Single(await check.AdminSessions.Where(s => s.RevokedAt == null).ToListAsync());
        Assert.Equal("private-note-postgres-sentinel", (await check.Notes.SingleAsync()).Body);
    }
}
