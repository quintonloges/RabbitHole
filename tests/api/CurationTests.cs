using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class CurationTests
{
    [Theory]
    [InlineData("consciousness", "all:consciousness")]
    [InlineData("ti:\"machine consciousness\" OR abs:\"phenomenal consciousness\"", "ti:\"machine consciousness\" OR abs:\"phenomenal consciousness\"")]
    [InlineData("cat:cs.RO AND (ti:embodied OR abs:\"world model\")", "cat:cs.RO AND (ti:embodied OR abs:\"world model\")")]
    public void Arxiv_preserves_field_queries_and_bounds_results(string query, string expected)
    {
        var parameters = QueryHelpers.ParseQuery(ProviderAdapters.ArxivSearchUrl(query, 1000).Query);
        Assert.Equal(expected, parameters["search_query"].ToString());
        Assert.Equal("25", parameters["max_results"].ToString());
        Assert.Equal("submittedDate", parameters["sortBy"].ToString());
    }

    [Fact]
    public void Openalex_title_watch_uses_native_scope_and_publication_window()
    {
        var end = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);
        var query = QueryHelpers.ParseQuery(ProviderAdapters.OpenAlexWatchUrl("title:panpsychism OR idealism", 6, null, end.AddYears(-2), null, end).Query);
        Assert.Equal("panpsychism OR idealism", query["search.title"].ToString());
        Assert.False(query.ContainsKey("search.title_and_abstract"));
        Assert.Equal("from_publication_date:2024-10-17,to_publication_date:2026-10-17", query["filter"].ToString());
    }

    [Fact]
    public void Atom_keeps_multiple_creators()
    {
        var item = Assert.Single(ProviderAdapters.ParseArxiv("<feed><entry><title>TEST</title><link href='https://arxiv.org/abs/1706.03762'/><author><name>Ada</name><email>ada@example.org</email><uri>https://example.org</uri></author><author><name>Ben</name></author></entry></feed>"));
        Assert.Equal("Ada, Ben", item.Author);
    }

    [Fact]
    public void Feed_excerpt_omits_styles_scripts_and_comments_before_truncation()
    {
        var item = Assert.Single(ProviderAdapters.ParseFeed("""
            <rss><channel><item><title>TEST</title><link>https://example.org/a</link><description><![CDATA[
            <style>.fig { width: 100%; }</style><script>console.log('noise')</script><!-- metadata -->
            <p>Actual supplied research summary.</p>]]></description></item></channel></rss>
            """));
        Assert.Equal("Actual supplied research summary.", item.Excerpt);
    }

    [Fact]
    public async Task Related_prefers_subject_phrase_to_disconnected_title_words()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var topic = new Topic { Name = "TEST AI" };
        var start = new ContentItem { Title = "A global workspace in language models", TopicIds = [topic.Id], CanonicalUrl = "https://example.org/start" };
        var subject = new ContentItem { Title = "Mind modulation systems and global workspace theory", Excerpt = "Consciousness and neural cognition", TopicIds = [topic.Id], CanonicalUrl = "https://example.net/subject" };
        var incidental = new ContentItem { Title = "Global geospatial models for public health", TopicIds = [topic.Id], CanonicalUrl = "https://example.org/incidental" };
        db.AddRange(topic, start, subject, incidental); await db.SaveChangesAsync();
        var related = await new Ranking(db).RelatedAsync(start, 8, default);
        Assert.Equal(subject.Id, related[0].Item.Id);
        Assert.Contains("global workspace", related[0].Reason);
    }

    [Fact]
    public void Manual_metadata_preserves_attribution_and_source_publication_date()
    {
        var metadata = ProviderAdapters.HtmlMetadata("""
            <meta content="An &amp; argument" property="og:title">
            <meta name="author" content="Ada &amp; Ben">
            <meta property="article:published_time" content="1995-06-01">
            <meta property="article:modified_time" content="2026-10-17">
            """);
        Assert.Equal("An & argument", metadata.Title);
        Assert.Equal("Ada & Ben", metadata.Author);
        Assert.Equal(new DateTimeOffset(1995, 6, 1, 0, 0, 0, TimeSpan.Zero), metadata.PublishedAt);
    }

    [Theory]
    [InlineData("<meta property='article:modified_time' content='2026-10-17'>")]
    [InlineData("<meta property='article:published_time' content='not-a-date'>")]
    public void Manual_metadata_does_not_guess_missing_dates(string html) =>
        Assert.Null(ProviderAdapters.HtmlMetadata(html).PublishedAt);

    [Fact]
    public async Task Related_favors_specific_terms_over_broad_tags_and_drops_stopword_only_links()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var topic = new Topic { Name = "TEST philosophy" };
        var start = new ContentItem { Title = "Global workspace consciousness", Excerpt = "The study of neural broadcasting", TopicIds = [topic.Id], CanonicalUrl = "https://example.org/start" };
        var specific = new ContentItem { Title = "Global workspace and neural broadcasting", TopicIds = [], CanonicalUrl = "https://example.net/specific" };
        var broad = new ContentItem { Title = "Ethics and moral duties", TopicIds = [topic.Id], CanonicalUrl = "https://example.org/broad" };
        var noise = new ContentItem { Title = "The new study", Excerpt = "This paper presents the results", CanonicalUrl = "https://example.org/noise" };
        var disabled = new Source { Name = "TEST disabled", Enabled = false };
        var suppressed = new ContentItem { Title = start.Title, CanonicalUrl = "https://example.org/disabled", SourceId = disabled.Id };
        db.AddRange(topic, start, specific, broad, noise, disabled, suppressed); await db.SaveChangesAsync();
        var related = await new Ranking(db).RelatedAsync(start, 8, default);
        Assert.Equal(specific.Id, related[0].Item.Id);
        Assert.Contains("global", related[0].Reason);
        Assert.All(related, result => Assert.Equal(new[] { start.Id }, result.Evidence));
        Assert.DoesNotContain(related, result => result.Item.Id == noise.Id || result.Item.Id == suppressed.Id);
        Assert.Contains(related, result => result.Item.Id == broad.Id && result.Reason.Contains("broad topic"));
    }

    [Fact]
    public async Task Daily_selection_caps_a_prolific_source_when_alternatives_exist()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var topic = new Topic { Name = "TEST interest", Weight = 5 };
        db.Add(topic);
        for (var source = 0; source < 6; source++)
        {
            var subscription = new Source { Name = "TEST source " + source };
            db.Add(subscription);
            for (var i = 0; i < (source == 0 ? 40 : 3); i++) db.Add(new ContentItem
            {
                Title = $"TEST {source} {i}", SourceId = subscription.Id, TopicIds = [topic.Id],
                CanonicalUrl = $"https://example.org/{source}/{i}", PublishedAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();
        var selected = await new Ranking(db).SelectAsync(12, "fixed", null, default);
        Assert.Equal(12, selected.Count);
        Assert.All(selected.GroupBy(x => x.Item.SourceId), group => Assert.True(group.Count() <= 2));
    }

    [Fact]
    public async Task Unknown_publication_date_does_not_receive_a_fresh_news_bonus()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var recent = new ContentItem { Title = "TEST recent", CanonicalUrl = "https://example.org/recent", PublishedAt = DateTimeOffset.UtcNow };
        var unknown = new ContentItem { Title = "TEST unknown", CanonicalUrl = "https://example.org/unknown", IngestedAt = DateTimeOffset.UtcNow };
        db.AddRange(recent, unknown); await db.SaveChangesAsync();
        var selection = await new Ranking(db).SelectAsync(1, "fixed", null, default);
        Assert.Equal(recent.Id, selection[0].Item.Id);
    }
}
