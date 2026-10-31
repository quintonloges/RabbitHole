using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitHole.Data;
using RabbitHole.Ingestion;
using RabbitHole.Features;

namespace RabbitHole.E2e;

public static class Runner
{
    public static async Task Main(string[] args)
    {
        using var factory = new FixtureFactory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 5081));
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AdminAuthentication>().SetPasswordAsync("TEST-FIXTURE-password-2026!");
        Console.WriteLine("TEST FIXTURE API listening at http://127.0.0.1:5081; external HTTP is replaced; private production data is not used.");
        await Task.Delay(Timeout.Infinite);
    }
}

public class FixtureFactory : WebApplicationFactory<global::Program>
{
    private readonly string temporary = Path.Combine(Path.GetTempPath(), "rabbithole-e2e-" + Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(temporary);
        builder.UseEnvironment("Testing");
        builder.UseSetting("Security:DataProtectionPath", Path.Combine(temporary, "auth-keys"));
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../apps/api")));
        builder.UseSetting("DATABASE_PROVIDER", "sqlite");
        builder.UseSetting("ConnectionStrings:Default", "Data Source=" + Path.Combine(temporary, "test.db"));
        builder.UseSetting("Documents:Path", Path.Combine(temporary, "uploads"));
        builder.UseSetting("AllowedOrigin", "http://127.0.0.1:3001");
        builder.UseSetting("RABBITHOLE_ALLOWED_ORIGIN", "http://127.0.0.1:3001");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRemoteFetcher>();
            services.AddSingleton<IRemoteFetcher, FixtureFetcher>();
        });
    }
}

/// <summary>Only external network boundary is replaced. Not referenced by the production project.</summary>
public class FixtureFetcher : IRemoteFetcher
{
    public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken)
    {
        var url = request.Url;
        string body; string mime;
        if (url.Host == "example.org" && url.AbsolutePath == "/fixture-feed.xml")
        {
            mime = "application/rss+xml";
            body = """
                <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/"><channel><title>TEST FIXTURE source</title><link>https://example.org</link>
                <item><guid>fixture-a</guid><title>TEST FIXTURE: Consciousness and machines</title><link>https://example.org/fixture-a</link><author>Fixture author</author><pubDate>Mon, 01 Jun 2020 12:00:00 GMT</pubDate><description>A deterministic test about consciousness and philosophy.</description><content:encoded><![CDATA[<p>This TEST FIXTURE discusses consciousness, philosophy, and how a reader compares accounts of experience. This is test source text, never production feed content.</p>]]></content:encoded></item>
                <item><guid>fixture-b</guid><title>TEST FIXTURE: Myth and memory</title><link>https://example.org/fixture-b</link><description>A test passage about myths, anthropology and philosophy.</description></item>
                <item><guid>fixture-c</guid><title>TEST FIXTURE: Architectural tradeoffs</title><link>https://example.org/fixture-c</link><description>A supplied source discussing software engineering, architecture, systems design and learning from alternate designs.</description></item>
                </channel></rss>
                """;
        }
        else if (url.Host == "hn.algolia.com")
        {
            mime = "application/json";
            body = """
                {"nbPages":1,"hits":[{"objectID":"101","title":"TEST FIXTURE: How oscillators work (1999)","url":"https://example.org/archive-fixture/discovered-online","created_at_i":1262304000,"author":"Not the original author"}]}
                """;
        }
        else if (url.Host == "api.openalex.org")
        {
            mime = "application/json";
            var item = """
                {"id":"https://openalex.org/W1234567890","doi":"https://doi.org/10.1234/rabbithole.fixture","title":"TEST FIXTURE: A consciousness paper","publication_date":"2001-02-03","authorships":[{"author":{"display_name":"Fixture researcher"}}],"primary_location":{"landing_page_url":"https://example.org/fixture-paper"},"abstract_inverted_index":{"This":[0],"fixture":[1],"abstract":[2],"discusses":[3],"consciousness":[4]}}
                """;
            body = url.AbsolutePath.Contains("W1234567890") ? item : "{\"results\":[" + item + "]}";
        }
        else if (url.Host == "export.arxiv.org")
        {
            mime = "application/atom+xml";
            body = """
                <feed xmlns="http://www.w3.org/2005/Atom"><entry><id>http://arxiv.org/abs/2401.00001v1</id><title>TEST FIXTURE: A robotics paper</title><author><name>Fixture scientist</name></author><published>2024-01-01T00:00:00Z</published><summary>Robotics and software engineering fixture abstract.</summary><link href="https://arxiv.org/abs/2401.00001" rel="alternate"/></entry></feed>
                """;
        }
        else if (url.Host.EndsWith("youtube.com") && url.AbsolutePath == "/oembed")
        {
            mime = "application/json";
            body = """{"title":"TEST FIXTURE: Video","author_name":"Fixture creator","type":"video","html":"<iframe src='https://www.youtube.com/embed/dQw4w9WgXcQ'></iframe>"}""";
        }
        else if (url.Host.EndsWith("soundcloud.com") && url.AbsolutePath == "/oembed")
        {
            mime = "application/json";
            body = """{"title":"TEST FIXTURE: Track","author_name":"Fixture artist","type":"rich","html":"<iframe src='https://w.soundcloud.com/player/?url=https%3A//api.soundcloud.com/tracks/123456'></iframe>"}""";
        }
        else if (url.AbsolutePath == "/robots.txt") { mime = "text/plain"; body = "User-agent: *\nAllow: /"; }
        else if (url.Host == "example.org" && url.AbsolutePath.StartsWith("/archive-fixture/", StringComparison.Ordinal))
        {
            mime = "text/html";
            body = "<html><head><title>TEST FIXTURE: How oscillators work</title><meta name='description' content='An explanation of oscillators and waveforms, with sound design techniques.'/><meta property='article:published_time' content='2001-02-03T00:00:00Z'/></head><body>TEST FIXTURE only.</body></html>";
        }
        else if (url.Host == "example.org")
        {
            mime = "text/html";
            body = "<html><head><title>TEST FIXTURE: Imported link</title><meta name='description' content='A fixture metadata description.'/></head><body>No body extraction.</body></html>";
        }
        else throw new RemoteFetchException("TEST FIXTURE: unavailable external provider.");
        return Task.FromResult(new RemoteResponse(200, url, mime, Encoding.UTF8.GetBytes(body), "fixture-etag", null));
    }
}
