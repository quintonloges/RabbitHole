using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;
using RabbitHole.Features;
using Xunit;

namespace RabbitHole.Tests;

public class ItemPresentationTests
{
    [Theory]
    [InlineData("application/pdf", "page=7", 7, 7, "PDF", "page")]
    [InlineData("application/pdf", "bookmark:3-7", 3, 7, "PDF", "section")]
    [InlineData("application/pdf", "manual:9-9", 9, 9, "PDF", "section")]
    [InlineData("application/epub+zip", "OEBPS/chapter.xhtml#notes", null, null, "EPUB", "section")]
    [InlineData("application/pdf", "original", null, null, "PDF", "document")]
    public async Task List_items_identify_the_parent_and_section_without_exposing_files_or_copying_full_text(
        string mime, string locator, int? start, int? end, string format, string sectionType)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var document = new LibraryDocument { Title = "TEST FIXTURE parent book", Author = "Fixture author", MimeType = mime, PageCount = 369, StorageName = "private-storage-name", FileName = "private-original-name", Sha256 = "fixture-hash" };
        var section = new DocumentSection { DocumentId = document.Id, Title = "Owner-edited heading", Locator = locator, StartPage = start, EndPage = end, IsSuperseded = true };
        var item = new ContentItem { Title = section.Title, Kind = "book", DocumentId = document.Id, SectionId = section.Id, Body = "TEST FIXTURE full source text", Excerpt = "TEST FIXTURE excerpt", CanonicalUrl = $"local://document/{document.Id}/section/{section.Id}" };
        section.ItemId = item.Id;
        db.AddRange(document, section, item);
        await db.SaveChangesAsync();

        var dto = Assert.Single(await ItemDto.BuildAsync(db, [item], default));
        Assert.True(dto.HasText);
        Assert.Null(dto.Body);
        Assert.Null(dto.PublishedAt);
        Assert.Equal(item.Title, dto.Title);
        Assert.Equal(new ItemDocumentContext(document.Id, document.Title, document.Author, format, sectionType, start, end, 369, true, 1), dto.Document);
        var encoded = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("private-storage-name", encoded);
        Assert.DoesNotContain("private-original-name", encoded);
        Assert.DoesNotContain(item.Body, encoded);

        document.Title = "TEST FIXTURE renamed parent";
        await db.SaveChangesAsync();
        var refreshed = Assert.Single(await ItemDto.BuildAsync(db, [item], default));
        Assert.Equal(document.Title, refreshed.Document!.Title);
        Assert.Equal(item.Id, refreshed.Id);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(" \n\t ", false)]
    [InlineData("TEST FIXTURE supplied abstract", true)]
    public async Task Availability_describes_stored_text_even_when_a_list_omits_the_body(string? body, bool hasText)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var item = new ContentItem { Title = "TEST FIXTURE paper", Kind = "paper", Body = body, Excerpt = "A metadata excerpt is not a full abstract", CanonicalUrl = "https://example.org/fixture-paper", TextProvenance = "provider-abstract" };
        db.Add(item);
        await db.SaveChangesAsync();
        var dto = Assert.Single(await ItemDto.BuildAsync(db, [item], default));
        Assert.Equal(hasText, dto.HasText);
        Assert.Null(dto.Body);
        Assert.Null(dto.Document);
    }
}
