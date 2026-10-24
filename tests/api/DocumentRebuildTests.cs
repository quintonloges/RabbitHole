using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitHole.Data;
using RabbitHole.Domain;
using Xunit;

namespace RabbitHole.Tests;

[Collection("Document imports")]
public class DocumentRebuildTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "rabbithole-rebuild-" + Guid.NewGuid().ToString("N"));
        public string Original => Path.Combine(Folder, "uploads", "fixture.epub");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(Folder);
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("ConnectionStrings:Default", "Data Source=" + Path.Combine(Folder, "test.db"));
            builder.UseSetting("Documents:Path", Path.Combine(Folder, "uploads"));
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
        }
    }

    private static void WriteEpub(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Entry(string name, string text)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.NoCompression).Open());
            writer.Write(text);
        }
        Entry("mimetype", "application/epub+zip");
        Entry("META-INF/container.xml", """<container><rootfiles><rootfile full-path="OEBPS/book.opf"/></rootfiles></container>""");
        Entry("OEBPS/book.opf", """
            <package><metadata><title>TEST FIXTURE source title</title><creator>Source author</creator></metadata>
            <manifest><item id="content" href="content.xhtml" media-type="application/xhtml+xml"/>
            <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/></manifest>
            <spine><itemref idref="content"/></spine></package>
            """);
        Entry("OEBPS/nav.xhtml", """
            <html xmlns:epub="http://www.idpf.org/2007/ops"><body><nav epub:type="toc"><ol>
            <li><a href="content.xhtml#one">TEST FIXTURE First chapter</a></li>
            <li><a href="content.xhtml#two">TEST FIXTURE Second chapter</a></li>
            </ol></nav></body></html>
            """);
        Entry("OEBPS/content.xhtml", """
            <html><body><h1 id="one">TEST FIXTURE First chapter</h1><p>First source passage.</p>
            <h1 id="two">TEST FIXTURE Second chapter</h1><p>Second source passage.</p></body></html>
            """);
    }

    [Fact]
    public async Task Rebuild_splits_legacy_sections_without_moving_notes_progress_or_trail_links()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        WriteEpub(factory.Original);
        var doc = new LibraryDocument
        {
            Title = "TEST FIXTURE owner title",
            Author = "Owner author",
            FileName = "fixture.epub",
            StorageName = "fixture.epub",
            MimeType = "application/epub+zip",
            Status = "ready",
            Sha256 = Guid.NewGuid().ToString()
        };
        var oldSection = new DocumentSection
        {
            DocumentId = doc.Id,
            Title = "Section 1",
            Locator = "OEBPS/content.xhtml",
            Text = "TEST FIXTURE previous whole-file extraction with both chapters.",
            Order = 0
        };
        var oldItem = new ContentItem
        {
            Title = "Owner-edited section title",
            Kind = "book",
            Provider = "local-upload",
            DocumentId = doc.Id,
            SectionId = oldSection.Id,
            CanonicalUrl = $"local://document/{doc.Id}/section/{oldSection.Id}",
            Body = oldSection.Text
        };
        oldSection.ItemId = oldItem.Id;
        var note = new Note { ItemId = oldItem.Id, Body = "TEST FIXTURE retained note", Quote = "both chapters" };
        var hole = new RabbitHole.Domain.RabbitHole { Title = "TEST FIXTURE retained trail" };
        var node = new RabbitNode { HoleId = hole.Id, ItemId = oldItem.Id };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Documents.Add(doc); db.Items.Add(oldItem); db.Sections.Add(oldSection);
            db.Notes.Add(note); db.States.Add(new ItemState { ItemId = oldItem.Id, Saved = true, Read = true, Progress = .65 });
            db.Holes.Add(hole); db.Nodes.Add(node);
            await db.SaveChangesAsync();
        }
        (await client.PostAsync($"/api/library/books/{doc.Id}/retry", null)).EnsureSuccessStatusCode();
        Guid editedItemId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var current = await db.Sections.Where(s => !s.IsSuperseded).OrderBy(s => s.Order).ToListAsync();
            // Emulate old generic source labels and an independently edited reader title.
            current[0].Title = "Section 1";
            (await db.Items.SingleAsync(i => i.Id == current[0].ItemId)).Title = "Section 1";
            current[1].Title = "Section 2";
            editedItemId = current[1].ItemId;
            (await db.Items.SingleAsync(i => i.Id == editedItemId)).Title = "Owner's chapter title";
            await db.SaveChangesAsync();
        }
        for (var i = 0; i < 2; i++)
            (await client.PostAsync($"/api/library/books/{doc.Id}/retry", null)).EnsureSuccessStatusCode();
        List<DocumentSection> sections;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            sections = await db.Sections.OrderBy(s => s.Order).ToListAsync();
            Assert.Equal(3, sections.Count);
            Assert.Equal(new[] { "OEBPS/content.xhtml#one", "OEBPS/content.xhtml#two" }, sections.Where(s => !s.IsSuperseded).Select(s => s.Locator));
            Assert.Equal("TEST FIXTURE First chapter", (await db.Items.SingleAsync(i => i.Id == sections[0].ItemId)).Title);
            Assert.Equal("Owner's chapter title", (await db.Items.SingleAsync(i => i.Id == editedItemId)).Title);
            Assert.True(sections.Single(s => s.Id == oldSection.Id).IsSuperseded);
            Assert.Equal(oldSection.Text, sections.Single(s => s.Id == oldSection.Id).Text);
            Assert.Equal(oldItem.Title, (await db.Items.SingleAsync(i => i.Id == oldItem.Id)).Title);
            Assert.Equal(oldItem.Body, (await db.Items.SingleAsync(i => i.Id == oldItem.Id)).Body);
            Assert.Equal(note.Id, (await db.Notes.SingleAsync()).Id);
            Assert.Equal(oldItem.Id, (await db.Nodes.SingleAsync()).ItemId);
            var state = await db.States.SingleAsync();
            Assert.Equal(.65, state.Progress); Assert.True(state.Read); Assert.True(state.Saved);
            Assert.Equal(doc.Title, (await db.Documents.SingleAsync()).Title);
            Assert.Equal(doc.Author, (await db.Documents.SingleAsync()).Author);
        }
        var visible = await client.GetFromJsonAsync<List<DocumentSection>>($"/api/library/books/{doc.Id}/sections");
        Assert.NotNull(visible);
        Assert.Equal(sections.Select(s => s.Id), visible!.Select(s => s.Id));
        Assert.True(visible!.Single(s => s.Id == oldSection.Id).IsSuperseded);
        Assert.Equal("Owner's chapter title", visible.Single(s => s.ItemId == editedItemId).Title);
        (await client.GetAsync($"/api/items/{oldItem.Id}")).EnsureSuccessStatusCode();

        // A later parsing failure cannot hide the last successful chapter structure.
        await File.WriteAllTextAsync(factory.Original, "TEST FIXTURE broken EPUB");
        (await client.PostAsync($"/api/library/books/{doc.Id}/retry", null)).EnsureSuccessStatusCode();
        var afterFailure = await client.GetFromJsonAsync<List<DocumentSection>>($"/api/library/books/{doc.Id}/sections");
        Assert.Equal(sections.Select(s => (s.Id, s.Order, s.IsSuperseded)), afterFailure!.Select(s => (s.Id, s.Order, s.IsSuperseded)));
    }

    [Fact]
    public async Task Manual_range_reports_missing_pages_instead_of_creating_empty_section()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var doc = new LibraryDocument { Title = "TEST FIXTURE", MimeType = "application/pdf", PageCount = 2, Sha256 = Guid.NewGuid().ToString() };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync($"/api/library/books/{doc.Id}/sections", new { title = "Chapter", startPage = 1, endPage = 2 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await db.Sections.ToListAsync());
    }

    [Fact]
    public async Task Existing_sqlite_section_schema_upgrades_without_changing_records()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = new LibraryDocument { Title = "TEST FIXTURE", Sha256 = Guid.NewGuid().ToString() };
        var item = new ContentItem { Title = "TEST FIXTURE", CanonicalUrl = "local://fixture/schema", DocumentId = doc.Id };
        var section = new DocumentSection { DocumentId = doc.Id, ItemId = item.Id, Title = "Retained", Text = "TEST FIXTURE source" };
        db.Documents.Add(doc); db.Items.Add(item); db.Sections.Add(section);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Sections" DROP COLUMN "IsSuperseded";""");
        await SqliteCatalogSchema.ApplyAsync(db);
        await SqliteCatalogSchema.ApplyAsync(db);
        db.ChangeTracker.Clear();
        var retained = await db.Sections.SingleAsync();
        Assert.Equal(section.Id, retained.Id); Assert.Equal(section.Text, retained.Text);
        Assert.False(retained.IsSuperseded);
    }
}
