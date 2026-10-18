using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class ProviderRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArxivPreservesEntireSuppliedAbstractWithSeparateExcerpt(bool bytes)
    {
        var text = string.Join(' ', Enumerable.Range(0, 400).Select(i => "abstractword" + i));
        var xml = $"<feed xmlns='http://www.w3.org/2005/Atom'><entry><id>http://arxiv.org/abs/2401.00001</id><title>TEST FIXTURE paper</title><link href='https://arxiv.org/abs/2401.00001'/><summary>{text}</summary></entry></feed>";
        var parsed = Assert.Single(bytes ? ProviderAdapters.ParseArxiv(Encoding.UTF8.GetBytes(xml)) : ProviderAdapters.ParseArxiv(xml));
        Assert.Equal(text, parsed.Body);
        Assert.Equal(text[..1200], parsed.Excerpt);
        Assert.Equal("provider-abstract", parsed.TextProvenance);
        Assert.Equal("2401.00001", parsed.ArxivId);
        Assert.Null(parsed.PublishedAt);
    }

    [Fact]
    public void MissingArxivAbstractDoesNotClaimSuppliedSourceText()
    {
        var parsed = Assert.Single(ProviderAdapters.ParseArxiv("<feed><entry><title>TEST FIXTURE</title><link href='https://arxiv.org/abs/2401.00001'/></entry></feed>"));
        Assert.Null(parsed.Body);
        Assert.Equal("metadata", parsed.TextProvenance);
    }

    [Fact]
    public void RegularAtomSummaryRemainsAnExcerpt()
    {
        var parsed = Assert.Single(ProviderAdapters.ParseFeed("<feed><entry><title>TEST FIXTURE</title><link href='https://example.org/article'/><summary>Only a teaser</summary></entry></feed>"));
        Assert.Null(parsed.Body);
        Assert.Equal("Only a teaser", parsed.Excerpt);
        Assert.Equal("metadata", parsed.TextProvenance);
    }

    [Fact]
    public void WatchUsesChronologicalCursorAndInclusivePublicationDateFilter()
    {
        var url = ProviderAdapters.OpenAlexWatchUrl("mind & consciousness", 200, "test key",
            new DateTimeOffset(2026, 10, 15, 21, 0, 0, TimeSpan.Zero), "next+/cursor=");
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal("mind & consciousness", query["search.title_and_abstract"].ToString());
        Assert.Equal("publication_date:desc", query["sort"].ToString());
        Assert.Equal("from_publication_date:2026-10-15", query["filter"].ToString());
        Assert.Equal("next+/cursor=", query["cursor"].ToString());
        Assert.Equal("test key", query["api_key"].ToString());
        Assert.Equal("25", query["per_page"].ToString());
    }

    [Fact]
    public void InitialWatchUsesFirstCursorWhileInteractiveSearchRetainsRelevanceOrder()
    {
        var watch = QueryHelpers.ParseQuery(ProviderAdapters.OpenAlexWatchUrl("mind", 10, null).Query);
        Assert.Equal("*", watch["cursor"].ToString());
        Assert.False(watch.ContainsKey("filter"));
        Assert.False(watch.ContainsKey("api_key"));
        var search = QueryHelpers.ParseQuery(ProviderAdapters.OpenAlexSearchUrl("mind", 10, null).Query);
        Assert.False(search.ContainsKey("sort"));
        Assert.False(search.ContainsKey("cursor"));
        Assert.False(search.ContainsKey("filter"));
    }

    [Fact]
    public void PageReportsRawResultCountAndCursorEvenWhenSomeRecordsAreUnusable()
    {
        var json = JsonSerializer.Serialize(new
        {
            meta = new { next_cursor = "next+/=", count = 1000 },
            results = new[] { new { id = "https://openalex.org/W42", display_name = "TEST FIXTURE" }, new { id = "not-an-openalex-id", display_name = "invalid" } }
        });
        var page = ProviderAdapters.ParseOpenAlexPage(json);
        Assert.Equal(2, page.ResultCount);
        Assert.Equal("next+/=", page.NextCursor);
        Assert.Equal("W42", Assert.Single(page.Items).ProviderId);
        Assert.Equal("W42", Assert.Single(ProviderAdapters.ParseOpenAlex(json)).ProviderId);
    }

    [Fact]
    public void EmptyPageEndsCursorAndMalformedPageIsExplicit()
    {
        var page = ProviderAdapters.ParseOpenAlexPage("{\"meta\":{\"next_cursor\":null},\"results\":[]}");
        Assert.Equal(0, page.ResultCount);
        Assert.Null(page.NextCursor);
        Assert.Empty(page.Items);
        Assert.Throws<FormatException>(() => ProviderAdapters.ParseOpenAlexPage("{\"error\":\"unavailable\"}"));
    }
}
