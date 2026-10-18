using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class CatalogRegressionTests
{
    private sealed class Store : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AppDbContext Db { get; }
        public Store(params IInterceptor[] interceptors)
        {
            connection.Open();
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(interceptors).Options);
            Db.Database.EnsureCreated();
        }
        public IngestionService Service(IRemoteFetcher? fetcher = null) => new(Db,
            fetcher ?? new Fetcher(_ => throw new InvalidOperationException("Unexpected provider request in catalog fixture.")),
            new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class Fetcher(Func<RemoteRequest, RemoteResponse> fetch) : IRemoteFetcher
    {
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken) => Task.FromResult(fetch(request));
    }

    private static NormalizedItem Article(string url, string id = "same-opaque-guid") => new()
    {
        CanonicalUrl = url,
        Provider = "rss",
        ProviderId = id,
        Title = "TEST FIXTURE article"
    };

    [Fact]
    public async Task Opaque_feed_guids_are_scoped_to_their_source_and_cannot_overwrite_other_articles()
    {
        await using var store = new Store();
        var a = new Source { Name = "TEST FIXTURE A", Url = "https://example.org/feed.xml" };
        var b = new Source { Name = "TEST FIXTURE B", Url = "https://example.net/feed.xml" };
        store.Db.Sources.AddRange(a, b); await store.Db.SaveChangesAsync();
        var firstRecord = Article("https://example.org/first"); firstRecord.Title = "TEST FIXTURE first article";
        var secondRecord = Article("https://example.net/second"); secondRecord.Title = "TEST FIXTURE second article";
        var first = (await store.Service().UpsertAsync(firstRecord, a.Id, [], default)).Item;
        var note = new Note { ItemId = first.Id, Body = "TEST FIXTURE note about the first article" };
        store.Db.Notes.Add(note); await store.Db.SaveChangesAsync();
        var second = (await store.Service().UpsertAsync(secondRecord, b.Id, [], default)).Item;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await store.Db.Items.CountAsync());
        Assert.Equal("TEST FIXTURE first article", (await store.Db.Items.SingleAsync(i => i.Id == first.Id)).Title);
        Assert.Equal(first.Id, (await store.Db.Notes.SingleAsync()).ItemId);
        await store.Service().UpsertAsync(firstRecord, a.Id, [], default);
        await store.Service().UpsertAsync(secondRecord, b.Id, [], default);
        Assert.Equal(2, await store.Db.Items.CountAsync());
        Assert.Equal(2, await store.Db.ItemSources.CountAsync());
    }

    [Fact]
    public async Task Shared_url_keeps_both_origins_and_remains_discoverable_while_either_source_is_enabled()
    {
        await using var store = new Store();
        var a = new Source { Name = "TEST FIXTURE primary", Url = "https://example.org/a.xml" };
        var b = new Source { Name = "TEST FIXTURE secondary", Url = "https://example.org/b.xml" };
        store.Db.Sources.AddRange(a, b); await store.Db.SaveChangesAsync();
        var record = Article("https://example.org/shared");
        var first = (await store.Service().UpsertAsync(record, a.Id, [], default)).Item;
        var second = (await store.Service().UpsertAsync(record, b.Id, [], default)).Item;
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await store.Db.Items.ToListAsync());
        Assert.Equal(new[] { a.Id, b.Id }.Order(), (await store.Db.ItemSources.ToListAsync()).Select(x => x.SourceId!.Value).Order());
        a.Enabled = false; await store.Db.SaveChangesAsync();
        Assert.Equal(first.Id, Assert.Single(await new Ranking(store.Db).SelectAsync(10, "fixture", null, default)).Item.Id);
        b.Enabled = false; await store.Db.SaveChangesAsync();
        Assert.Empty(await new Ranking(store.Db).SelectAsync(10, "fixture", null, default));
    }

    [Fact]
    public async Task Explicitly_removed_topics_stay_removed_after_the_same_source_refreshes()
    {
        await using var store = new Store();
        var topic = new Topic { Name = "TEST FIXTURE philosophy", Keywords = "consciousness" };
        var source = new Source { Name = "TEST FIXTURE source", Url = "https://example.org/feed", TopicIds = [topic.Id] };
        store.Db.AddRange(topic, source); await store.Db.SaveChangesAsync();
        var record = Article("https://example.org/mind"); record.Title = "TEST FIXTURE consciousness";
        var item = (await store.Service().UpsertAsync(record, source.Id, source.TopicIds, default)).Item;
        Assert.Contains(topic.Id, item.TopicIds);
        await TopicClassification.SetUserTopicsAsync(store.Db, item, [], default);
        await store.Db.SaveChangesAsync();
        await store.Service().UpsertAsync(record, source.Id, source.TopicIds, default);
        Assert.Empty(item.TopicIds);
        var edge = await store.Db.ItemTopics.SingleAsync(x => x.ItemId == item.Id && x.TopicId == topic.Id);
        Assert.Equal(false, edge.UserOverride);
        Assert.True(edge.SourceAssigned); Assert.True(edge.KeywordMatched);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("excerpt")]
    [InlineData("body")]
    public async Task External_items_match_keywords_in_title_excerpt_and_body_without_inherited_topics(string field)
    {
        await using var store = new Store();
        var topic = new Topic { Name = "TEST FIXTURE interest", Keywords = "consciousness" };
        var source = new Source { Name = "TEST FIXTURE source", Url = "https://example.org/feed" };
        store.Db.AddRange(topic, source); await store.Db.SaveChangesAsync();
        var record = Article("https://example.org/keyword");
        if (field == "title") record.Title = "TEST FIXTURE consciousness research";
        if (field == "excerpt") record.Excerpt = "TEST FIXTURE consciousness excerpt";
        if (field == "body") { record.Body = "TEST FIXTURE consciousness source text"; record.TextProvenance = "feed-content"; }
        var item = (await store.Service().UpsertAsync(record, source.Id, [], default)).Item;
        Assert.Contains(topic.Id, item.TopicIds);
        Assert.True((await store.Db.ItemTopics.SingleAsync(x => x.ItemId == item.Id && x.TopicId == topic.Id)).KeywordMatched);
    }

    [Fact]
    public async Task Editing_keywords_reclassifies_existing_items_and_preserves_explicit_removals()
    {
        await using var store = new Store();
        var topic = new Topic { Name = "TEST FIXTURE interest", Keywords = "consciousness" };
        store.Db.Topics.Add(topic); await store.Db.SaveChangesAsync();
        var oldRecord = Article("https://example.org/old", "old"); oldRecord.Excerpt = "TEST FIXTURE consciousness";
        var newRecord = Article("https://example.org/new", "new"); newRecord.Body = "TEST FIXTURE quantum physics";
        var removedRecord = Article("https://example.org/removed", "removed"); removedRecord.Body = "TEST FIXTURE quantum physics";
        var oldItem = (await store.Service().UpsertAsync(oldRecord, null, [], default)).Item;
        var newItem = (await store.Service().UpsertAsync(newRecord, null, [], default)).Item;
        var removedItem = (await store.Service().UpsertAsync(removedRecord, null, [], default)).Item;
        await TopicClassification.SetUserTopicsAsync(store.Db, removedItem, [], default);
        await store.Db.SaveChangesAsync();
        Assert.Contains(topic.Id, oldItem.TopicIds); Assert.Empty(newItem.TopicIds);
        topic.Keywords = "quantum"; await store.Db.SaveChangesAsync();
        await TopicClassification.ReclassifyAsync(store.Db, default);
        Assert.Empty(oldItem.TopicIds);
        Assert.Contains(topic.Id, newItem.TopicIds);
        Assert.Empty(removedItem.TopicIds);
    }

    [Fact]
    public async Task Bridging_scholarly_identities_merges_active_catalog_without_losing_personal_history()
    {
        await using var store = new Store();
        var manual = new NormalizedItem { CanonicalUrl = "https://arxiv.org/abs/2401.00001", Provider = "manual", Title = "TEST FIXTURE manual link" };
        var openAlex = new NormalizedItem { CanonicalUrl = "https://openalex.org/W42", Provider = "openalex", ProviderId = "W42", Doi = "10.1234/fixture", Title = "TEST FIXTURE scholarly record" };
        var a = (await store.Service().UpsertAsync(manual, null, [], default)).Item;
        var b = (await store.Service().UpsertAsync(openAlex, null, [], default)).Item;
        a.IngestedAt = DateTimeOffset.Parse("2024-01-01T00:00:00Z"); b.IngestedAt = a.IngestedAt.AddDays(1);
        store.Db.States.AddRange(new ItemState { ItemId = a.Id, Saved = true, Progress = .2 },
            new ItemState { ItemId = b.Id, Read = true, Progress = .8 });
        var notes = new[] { new Note { ItemId = a.Id, Body = "TEST FIXTURE note A" }, new Note { ItemId = b.Id, Body = "TEST FIXTURE note B" } };
        store.Db.Notes.AddRange(notes);
        var hole = new RabbitHole.Domain.RabbitHole { Title = "TEST FIXTURE trail" };
        var node = new RabbitNode { HoleId = hole.Id, ItemId = b.Id, EvidenceItemIds = [a.Id, b.Id] };
        var prompt = new PracticePrompt { ItemId = b.Id, Question = "TEST FIXTURE question" };
        var attempt = new PracticeAttempt { PromptId = prompt.Id, Response = "TEST FIXTURE retained answer", Rating = "good" };
        var edition = new FeedEdition { Day = "fixture-history" };
        var entries = new[] { new FeedEntry { EditionId = edition.Id, ItemId = a.Id, Position = 0 }, new FeedEntry { EditionId = edition.Id, ItemId = b.Id, Position = 1 } };
        store.Db.AddRange(hole, node, prompt, attempt, edition); store.Db.Entries.AddRange(entries);
        await store.Db.SaveChangesAsync();
        var bridge = new NormalizedItem { CanonicalUrl = manual.CanonicalUrl, Provider = "arxiv", ProviderId = "2401.00001", ArxivId = "2401.00001", Doi = openAlex.Doi, Title = "TEST FIXTURE bridged paper" };
        var winner = (await store.Service().UpsertAsync(bridge, null, [], default)).Item;
        var loserId = winner.Id == a.Id ? b.Id : a.Id;
        Assert.Single(await store.Db.Items.ToListAsync());
        Assert.Equal(2, await store.Db.Items.IgnoreQueryFilters().CountAsync());
        Assert.Equal(winner.Id, (await store.Db.Items.IgnoreQueryFilters().SingleAsync(i => i.Id == loserId)).MergedIntoId);
        Assert.Equal(winner.Id, (await ItemCatalog.ResolveAsync(store.Db, a.Id, default))!.Id);
        Assert.Equal(winner.Id, (await ItemCatalog.ResolveAsync(store.Db, b.Id, default))!.Id);
        Assert.Equal(2, await store.Db.States.CountAsync());
        var aggregate = await store.Db.States.SingleAsync(s => s.ItemId == winner.Id);
        Assert.True(aggregate.Saved); Assert.True(aggregate.Read); Assert.Equal(.8, aggregate.Progress);
        var historicalState = await store.Db.States.SingleAsync(s => s.ItemId == loserId);
        Assert.Equal(loserId == a.Id ? .2 : .8, historicalState.Progress);
        Assert.Equal(loserId == a.Id, historicalState.Saved); Assert.Equal(loserId == b.Id, historicalState.Read);
        Assert.Equal(notes.Select(n => n.Id).Order(), (await store.Db.Notes.ToListAsync()).Select(n => n.Id).Order());
        Assert.All(await store.Db.Notes.ToListAsync(), n => Assert.Equal(winner.Id, n.ItemId));
        Assert.Equal(winner.Id, (await store.Db.Nodes.SingleAsync(n => n.Id == node.Id)).ItemId);
        Assert.Equal(winner.Id, (await store.Db.Prompts.SingleAsync(p => p.Id == prompt.Id)).ItemId);
        Assert.Equal(prompt.Id, (await store.Db.Attempts.SingleAsync(p => p.Id == attempt.Id)).PromptId);
        var history = await store.Db.Entries.OrderBy(e => e.Position).ToListAsync();
        Assert.Equal(entries.Select(e => e.Id), history.Select(e => e.Id));
        Assert.Equal(new[] { a.Id, b.Id }, history.Select(e => e.ItemId));
        foreach (var entry in history) Assert.Equal(winner.Id, (await ItemCatalog.ResolveAsync(store.Db, entry.ItemId, default))!.Id);
        var identityCount = await store.Db.ItemIdentities.CountAsync();
        await store.Service().UpsertAsync(bridge, null, [], default);
        await store.Service().UpsertAsync(openAlex, null, [], default);
        await store.Service().UpsertAsync(manual, null, [], default);
        Assert.Single(await store.Db.Items.ToListAsync());
        Assert.Equal(identityCount, await store.Db.ItemIdentities.CountAsync());
        Assert.Equal(2, await store.Db.Notes.CountAsync());
        Assert.Equal(2, await store.Db.Entries.CountAsync());
    }

    private sealed class FailOneItemSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<ContentItem>().Any(e => e.State is EntityState.Added or EntityState.Modified))
            {
                Armed = false;
                throw new DbUpdateException("TEST FIXTURE simulated persistence failure");
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Item_persistence_failure_records_failed_job_and_does_not_poison_later_refresh_in_same_context()
    {
        var interceptor = new FailOneItemSave();
        await using var store = new Store(interceptor);
        var bad = new Source { Name = "TEST FIXTURE failed write", Url = "https://bad.example/feed" };
        var good = new Source { Name = "TEST FIXTURE healthy", Url = "https://good.example/feed" };
        store.Db.Sources.AddRange(bad, good); await store.Db.SaveChangesAsync();
        var fetcher = new Fetcher(request => new RemoteResponse(200, request.Url, "application/rss+xml", Encoding.UTF8.GetBytes(
            $"<rss><channel><item><guid>fixture</guid><title>TEST FIXTURE</title><link>https://{request.Url.Host}/article</link></item></channel></rss>"), null, null));
        var service = store.Service(fetcher);
        interceptor.Armed = true;
        var failed = await service.RefreshAsync(bad, default);
        Assert.Equal("failed", failed.Status);
        Assert.NotNull(failed.FinishedAt); Assert.NotNull(failed.Error);
        Assert.Equal("failed", (await store.Db.Jobs.AsNoTracking().SingleAsync(j => j.Id == failed.Id)).Status);
        var failedSource = await store.Db.Sources.AsNoTracking().SingleAsync(s => s.Id == bad.Id);
        Assert.Equal(1, failedSource.FailureCount);
        Assert.True(failedSource.NextSyncAt > DateTimeOffset.UtcNow);
        var healthy = await service.RefreshAsync(good, default);
        Assert.Equal("completed", healthy.Status);
        var healthySource = await store.Db.Sources.AsNoTracking().SingleAsync(s => s.Id == good.Id);
        Assert.NotNull(healthySource.LastSuccessAt);
        Assert.Null(healthySource.LastError);
        Assert.Equal(1, healthySource.LastImportedCount);
        Assert.Equal("https://good.example/article", (await store.Db.Items.SingleAsync()).CanonicalUrl);
        Assert.Equal(2, await store.Db.Jobs.CountAsync());
    }
}
