using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitHole.Data;
using RabbitHole.Domain;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace RabbitHole.Tests;

[Collection("Document imports")]
public class DocumentRecoveryTests
{
    private sealed class Factory : AuthenticatedApiFactory
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "rabbithole-document-recovery-" + Guid.NewGuid().ToString("N"));
        public string Original => Path.Combine(Folder, "uploads", "interrupted.pdf");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            Directory.CreateDirectory(Folder);
            builder.UseSetting("DATABASE_PROVIDER", "sqlite");
            builder.UseSetting("ConnectionStrings:Default", "Data Source=" + Path.Combine(Folder, "test.db"));
            builder.UseSetting("Documents:Path", Path.Combine(Folder, "uploads"));
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
        }
        public async Task<Guid> SeedAsync(byte[] bytes, string status = "parsing", bool originalExists = true)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Directory.CreateDirectory(Path.GetDirectoryName(Original)!);
            if (originalExists) await File.WriteAllBytesAsync(Original, bytes);
            var document = new LibraryDocument
            {
                Title = "TEST FIXTURE owner title",
                Author = "Owner-edited author",
                FileName = "fixture.pdf",
                StorageName = "interrupted.pdf",
                MimeType = "application/pdf",
                Status = status,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                SizeBytes = bytes.Length
            };
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            return document.Id;
        }
    }

    private static byte[] Pdf()
    {
        var writer = new PdfDocumentBuilder();
        var page = writer.AddPage(PageSize.A4);
        var font = writer.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("TEST FIXTURE: Recover the actual source text after interruption.", 12, new PdfPoint(30, 700), font);
        return writer.Build();
    }

    private static async Task<JsonElement> UploadAsync(HttpClient client, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/pdf");
        form.Add(content, "file", "fixture.pdf");
        using var response = await client.PostAsync("/api/library/books/upload", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Reupload_recovers_a_document_left_parsing_after_a_restart()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var bytes = Pdf();
        var id = await factory.SeedAsync(bytes);
        var result = await UploadAsync(client, bytes);
        Assert.True(result.GetProperty("duplicate").GetBoolean());
        Assert.Equal(id, result.GetProperty("document").GetProperty("id").GetGuid());
        Assert.Equal("ready", result.GetProperty("document").GetProperty("status").GetString());
        Assert.Single(result.GetProperty("sections").EnumerateArray());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Documents.ToListAsync());
        Assert.Contains("actual source text", (await db.Items.SingleAsync()).Body);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(factory.Original));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(factory.Original)!));
    }

    [Fact]
    public async Task Repeated_retry_preserves_sections_items_manual_ranges_and_personal_state()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var id = await factory.SeedAsync(Pdf(), "failed");
        var initial = await client.PostAsync($"/api/library/books/{id}/retry", null);
        initial.EnsureSuccessStatusCode();
        Guid sectionId, itemId, noteId, manualSectionId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var section = await db.Sections.SingleAsync();
            sectionId = section.Id; itemId = section.ItemId;
            var item = await db.Items.SingleAsync();
            item.ClassificationOrigin = "user"; item.TopicIds = [];
            db.States.Add(new ItemState { ItemId = itemId, Saved = true, Read = true, Progress = .7 });
            var note = new Note { ItemId = itemId, Body = "TEST FIXTURE retained note", Quote = "actual source text", Locator = "page=1" };
            noteId = note.Id;
            db.Notes.Add(note);
            await db.SaveChangesAsync();
        }
        var range = await client.PostAsJsonAsync($"/api/library/books/{id}/sections", new { title = "Owner chapter", startPage = 1, endPage = 1 });
        range.EnsureSuccessStatusCode();
        manualSectionId = (await range.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        for (var attempt = 0; attempt < 2; attempt++)
            (await client.PostAsync($"/api/library/books/{id}/retry", null)).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var doc = await db.Documents.SingleAsync();
            Assert.Equal("TEST FIXTURE owner title", doc.Title);
            Assert.Equal("Owner-edited author", doc.Author);
            Assert.Equal("ready", doc.Status);
            Assert.Equal(2, await db.Sections.CountAsync());
            Assert.Equal(2, await db.Items.CountAsync());
            Assert.Equal(itemId, (await db.Sections.SingleAsync(s => s.Id == sectionId)).ItemId);
            Assert.Equal("Owner chapter", (await db.Sections.SingleAsync(s => s.Id == manualSectionId)).Title);
            var state = await db.States.SingleAsync();
            Assert.True(state.Saved); Assert.True(state.Read); Assert.Equal(.7, state.Progress);
            Assert.Equal(noteId, (await db.Notes.SingleAsync()).Id);
            Assert.Equal("user", (await db.Items.SingleAsync(i => i.Id == itemId)).ClassificationOrigin);
            Assert.Empty((await db.Items.SingleAsync(i => i.Id == itemId)).TopicIds);
        }
    }

    [Fact]
    public async Task Failed_retry_retains_existing_extracted_text_and_provides_original_fallback_once()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var id = await factory.SeedAsync(Encoding.UTF8.GetBytes("%PDF-TEST FIXTURE invalid document"));
        var first = await client.PostAsync($"/api/library/books/{id}/retry", null);
        first.EnsureSuccessStatusCode();
        var result = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("failed", result.GetProperty("document").GetProperty("status").GetString());
        Assert.Contains("original file", result.GetProperty("document").GetProperty("error").GetString());
        var sectionId = result.GetProperty("sections")[0].GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var section = await db.Sections.SingleAsync();
            section.Text = "TEST FIXTURE previously extracted text";
            var item = await db.Items.SingleAsync(); item.Body = section.Text;
            await db.SaveChangesAsync();
        }
        (await client.PostAsync($"/api/library/books/{id}/retry", null)).EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(sectionId, (await db.Sections.SingleAsync()).Id);
            Assert.Equal("TEST FIXTURE previously extracted text", (await db.Items.SingleAsync()).Body);
        }
        (await client.GetAsync($"/api/library/books/{id}/file")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Missing_original_is_reported_and_matching_reupload_restores_same_document()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var bytes = Pdf();
        var id = await factory.SeedAsync(bytes, originalExists: false);
        var missing = await client.PostAsync($"/api/library/books/{id}/retry", null);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        Assert.Contains("same file", await missing.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("failed", (await db.Documents.SingleAsync()).Status);
            Assert.Empty(await db.Sections.ToListAsync());
        }
        var recovered = await UploadAsync(client, bytes);
        Assert.Equal(id, recovered.GetProperty("document").GetProperty("id").GetGuid());
        Assert.Equal("ready", recovered.GetProperty("document").GetProperty("status").GetString());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(factory.Original));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(factory.Original)!));
    }
}
