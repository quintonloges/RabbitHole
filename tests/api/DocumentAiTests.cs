using System.IO.Compression;
using System.Text.Json;
using RabbitHole.Features;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace RabbitHole.Tests;

public class DocumentAiTests
{
    [Fact]
    public void Pdf_extracts_actual_page_text_and_has_scanned_fallback()
    {
        var path = Path.GetTempFileName();
        try
        {
            var writer = new PdfDocumentBuilder();
            var page = writer.AddPage(PageSize.A4);
            var font = writer.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText("TEST FIXTURE: A small real PDF with extracted text.", 12, new PdfPoint(30, 700), font);
            File.WriteAllBytes(path, writer.Build());
            var result = DocumentParser.ParsePdf(path);
            Assert.Equal(1, result.PageCount);
            Assert.Contains("TEST FIXTURE", result.Sections[0].Text);
            Assert.Equal(1, result.Sections[0].StartPage);
            var blank = new PdfDocumentBuilder(); blank.AddPage(PageSize.A4);
            File.WriteAllBytes(path, blank.Build());
            Assert.Contains("OCR", DocumentParser.ParsePdf(path).Warning);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Epub_uses_manifest_spine_and_actual_toc_labels_and_ignores_scripts()
    {
        var path = MakeEpub();
        try
        {
            var parsed = DocumentParser.ParseEpub(path);
            Assert.Equal("TEST FIXTURE book", parsed.Title);
            Assert.Equal("Test author", parsed.Author);
            Assert.Equal(new[] { "An actual second chapter", "An actual first chapter" }, parsed.Sections.Select(s => s.Title));
            Assert.Contains("Second body", parsed.Sections[0].Text);
            Assert.DoesNotContain("alert", parsed.Sections[0].Text);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:\\escape.txt")]
    public void Epub_rejects_unsafe_archive_paths(string entry)
    {
        var path = MakeEpub();
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("unsafe");
            Assert.Throws<InvalidDataException>(() => DocumentParser.ParseEpub(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Epub_rejects_high_compression_and_drm()
    {
        var path = MakeEpub();
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            using (var writer = new StreamWriter(archive.CreateEntry("bomb.txt", CompressionLevel.SmallestSize).Open())) writer.Write(new string('a', 2_000_000));
            Assert.Throws<InvalidDataException>(() => DocumentParser.ParseEpub(path));
        }
        finally { File.Delete(path); }
        path = MakeEpub();
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update)) archive.CreateEntry("META-INF/encryption.xml");
            Assert.Throws<InvalidDataException>(() => DocumentParser.ParseEpub(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Ai_output_requires_known_evidence_and_rejects_fabricated_links_or_quotes()
    {
        var id = Guid.NewGuid();
        var evidence = new[] { new AiEvidence(id, "test", "The actual excerpt states an assertion.") };
        string Output(Guid reference, string text = "The source makes an assertion.", string? quote = null) => JsonSerializer.Serialize(new
        { text, evidenceItemIds = new[] { reference }, quotes = quote == null ? Array.Empty<object>() : [new { itemId = reference, text = quote }] });
        Assert.Equal(id, AiEndpoints.Validate(Output(id), evidence).EvidenceItemIds.Single());
        Assert.Throws<InvalidDataException>(() => AiEndpoints.Validate(Output(Guid.NewGuid()), evidence));
        Assert.Throws<InvalidDataException>(() => AiEndpoints.Validate(Output(id, "https://invented.example/paper"), evidence));
        Assert.Throws<InvalidDataException>(() => AiEndpoints.Validate(Output(id, quote: "invented quotation"), evidence));
        Assert.NotNull(AiEndpoints.Validate(Output(id, quote: "actual excerpt"), evidence));
        Assert.Throws<InvalidDataException>(() => AiEndpoints.Validate("{\"text\":\"No citations\",\"evidenceItemIds\":[]}", evidence));
    }

    public static string MakeEpub()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".epub");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        void Entry(string name, string text)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.NoCompression).Open());
            writer.Write(text);
        }
        Entry("mimetype", "application/epub+zip");
        Entry("META-INF/container.xml", """
            <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/book.opf" media-type="application/oebps-package+xml"/></rootfiles></container>
            """);
        Entry("OEBPS/book.opf", """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>TEST FIXTURE book</dc:title><dc:creator>Test author</dc:creator></metadata><manifest><item id="one" href="one.xhtml" media-type="application/xhtml+xml"/><item id="two" href="two.xhtml" media-type="application/xhtml+xml"/><item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/></manifest><spine><itemref idref="two"/><itemref idref="one"/></spine></package>
            """);
        Entry("OEBPS/nav.xhtml", """
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><body><nav epub:type="toc"><ol><li><a href="one.xhtml">An actual first chapter</a></li><li><a href="two.xhtml">An actual second chapter</a></li></ol></nav></body></html>
            """);
        Entry("OEBPS/one.xhtml", "<html><body><h1>First heading</h1><p>First body of the TEST FIXTURE book.</p></body></html>");
        Entry("OEBPS/two.xhtml", "<html><body><h1>Second heading</h1><p>Second body of the TEST FIXTURE book.</p><script>alert('untrusted')</script></body></html>");
        return path;
    }
}
