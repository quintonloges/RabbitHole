using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using RabbitHole.Features;
using Xunit;

namespace RabbitHole.Tests;

[Collection("Document imports")]
public class EpubReaderTests
{
    private sealed class Factory : AuthenticatedApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            Directory.CreateDirectory(SecurityFolder);
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("ConnectionStrings:Default", "Data Source=" + Path.Combine(SecurityFolder, "epub.db"));
            builder.UseSetting("Documents:Path", Path.Combine(SecurityFolder, "uploads"));
        }
    }

    private static byte[] Fixture() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "epub-reader.epub"));
    private static async Task<JsonElement> Upload(HttpClient client, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "TEST-FIXTURE-reader.epub");
        var response = await client.PostAsync("/api/library/books/upload", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Original_chapters_styles_and_images_are_private_sanitized_and_read_only()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var original = Fixture();
        var upload = await Upload(client, original);
        var id = upload.GetProperty("document").GetProperty("id").GetGuid();
        var itemId = upload.GetProperty("sections")[0].GetProperty("itemId").GetGuid();
        var stateBefore = (await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}")).GetProperty("state").ToString();
        var index = await client.GetFromJsonAsync<JsonElement>($"/api/library/books/{id}/epub");
        var chapters = index.GetProperty("chapters");
        Assert.Equal("Following a curious thought", chapters[0].GetProperty("title").GetString());
        Assert.Equal("A different perspective", chapters[1].GetProperty("title").GetString());
        var href = chapters[0].GetProperty("href").GetString()!;
        Assert.Equal(href, index.GetProperty("entryLocations").GetProperty(itemId.ToString()).GetString());
        var response = await client.GetAsync(href);
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/xhtml+xml", response.Content.Headers.ContentType!.MediaType);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'none'", csp);
        Assert.Contains("sandbox allow-same-origin", csp);
        Assert.Contains("form-action 'none'", csp);
        Assert.Contains("img-src 'self' data:", csp);
        var html = XDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("true", html.Root!.Attribute("data-epub-document")?.Value);
        Assert.DoesNotContain(html.Descendants(), el => el.Name.LocalName is "script" or "iframe" or "meta" or "form" or "input");
        Assert.DoesNotContain(html.Descendants().Attributes(), attr => attr.Name.LocalName.StartsWith("on"));
        var localImage = html.Descendants().First(el => el.Name.LocalName == "img" && el.Attribute("class") != null).Attribute("src")!.Value;
        var svg = await client.GetStringAsync(localImage);
        Assert.Contains("circle", svg);
        Assert.DoesNotContain("script", svg);
        var css = html.Descendants().Single(el => el.Name.LocalName == "link").Attribute("href")!.Value;
        Assert.Contains("font-size:19px", await client.GetStringAsync(css));
        Assert.DoesNotContain(html.Descendants().Attributes(), attr => attr.Name.LocalName is "src" or "href" && attr.Value.StartsWith("https://"));
        Assert.Contains(html.Descendants().Attributes(), attr => attr.Name.LocalName == "data-epub-external" && attr.Value == "https://example.com/book-reference");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(EpubReader.AssetUrl(id, "Book/bad.js"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(EpubReader.AssetUrl(id, "unlisted.xhtml"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(EpubReader.AssetUrl(id, "Book/package.opf"))).StatusCode);
        Assert.Equal(original, await client.GetByteArrayAsync($"/api/library/books/{id}/file"));
        Assert.Equal(stateBefore, (await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}")).GetProperty("state").ToString());
        using var anonymous = factory.CreateAnonymousClient();
        foreach (var url in new[] { $"/api/library/books/{id}/epub", href, localImage, css })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("META-INF/encryption.xml", "<encryption/>")]
    [InlineData("../outside.xhtml", "<html/>")]
    [InlineData("Book/chapters/one.xhtml", "duplicate ambiguous path")]
    public async Task Unsupported_archives_offer_original_file_without_exposing_resources(string path, string content)
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        using var buffer = new MemoryStream();
        buffer.Write(Fixture());
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry(path).Open())) writer.Write(content);
        var bytes = buffer.ToArray();
        var upload = await Upload(client, bytes);
        var id = upload.GetProperty("document").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync($"/api/library/books/{id}/epub")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(EpubReader.AssetUrl(id, "Book/chapters/one.xhtml"))).StatusCode);
        Assert.Equal(bytes, await client.GetByteArrayAsync($"/api/library/books/{id}/file"));
    }
}
