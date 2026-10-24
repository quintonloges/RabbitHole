using System.IO.Compression;
using RabbitHole.Features;
using Xunit;

namespace RabbitHole.Tests;

public class EpubSectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Toc_fragments_split_one_file_in_document_order_and_keep_front_matter(bool ncx)
    {
        using var book = new EpubFixture(ncx,
            [("two.xhtml", "<h1>Second spine file</h1><p>SECOND-FILE</p>"),
             ("one.xhtml", "<p>FRONT-MATTER</p><section><h1 id='first'>First heading</h1><p>FIRST-BODY</p><h1 id='second'>Second heading</h1><p>SECOND-BODY</p></section>")],
            [("one.xhtml#second", "Chapter two"), ("one.xhtml#first", "Chapter one"), ("two.xhtml", "Spine first")]);

        var result = DocumentParser.ParseEpub(book.Path);

        Assert.Null(result.Warning);
        Assert.Equal(["Spine first", "Front matter", "Chapter one", "Chapter two"], result.Sections.Select(s => s.Title));
        Assert.Equal(["OPS/two.xhtml", "epub-prefix:OPS/one.xhtml", "OPS/one.xhtml#first", "OPS/one.xhtml#second"], result.Sections.Select(s => s.Locator));
        Assert.Equal("FRONT-MATTER", result.Sections[1].Text);
        Assert.Contains("FIRST-BODY", result.Sections[2].Text);
        Assert.DoesNotContain("SECOND-BODY", result.Sections[2].Text);
        Assert.DoesNotContain("FIRST-BODY", result.Sections[3].Text);
        Assert.Equal(1, result.Sections.Count(s => s.Text.Contains("FRONT-MATTER")));
    }

    [Fact]
    public void Namespaced_xhtml_doctype_entities_accents_and_empty_named_anchors_are_supported()
    {
        using var book = new EpubFixture(false,
            [("chapters.xhtml", "<x:div><x:a name='début'/><x:h1>Élan &amp; café</x:h1><x:p>FIRST&nbsp;TEXT &mdash; déjà</x:p><x:a id='fin'/><x:h2>Fin</x:h2><x:p>LAST-TEXT</x:p><x:script>secretScript()</x:script><x:style>secretStyle</x:style></x:div>")],
            [("chapters.xhtml#d%C3%A9but", "Élan & café"), ("chapters.xhtml#fin", "Fin")], prefixed: true);

        var result = DocumentParser.ParseEpub(book.Path);

        Assert.Null(result.Warning);
        Assert.Equal(2, result.Sections.Count);
        Assert.Equal("Élan & café", result.Sections[0].Title);
        Assert.Equal("OPS/chapters.xhtml#début", result.Sections[0].Locator);
        Assert.Contains("FIRST\u00a0TEXT — déjà", result.Sections[0].Text);
        Assert.All(result.Sections, section => Assert.DoesNotContain("secret", section.Text));
    }

    [Fact]
    public void Nested_part_and_chapter_aliases_prefer_deeper_label_without_duplicate_text()
    {
        using var book = new EpubFixture(false,
            [("one.xhtml", "<section id='part'><h1 id='chapter'>Chapter heading</h1><p>ONCE-ONLY</p><h1 id='next'>Next</h1><p>NEXT-BODY</p></section>")], []);
        book.Replace("OPS/nav.xhtml", """
            <!DOCTYPE html>
            <html xmlns='http://www.w3.org/1999/xhtml' xmlns:epub='http://www.idpf.org/2007/ops'><body>
            <nav epub:type='toc'><ol><li><a href='one.xhtml#part'>Part I</a><ol>
            <li><a href='one.xhtml#chapter'><span>Real chapter</span></a></li>
            <li><a href='one.xhtml#chapter'>Duplicate chapter</a></li></ol></li>
            <li><a href='one.xhtml#next'>Next chapter</a></li></ol></nav></body></html>
            """);

        var result = DocumentParser.ParseEpub(book.Path);

        Assert.Equal(["Real chapter", "Next chapter"], result.Sections.Select(s => s.Title));
        Assert.Equal(1, result.Sections.Count(s => s.Text.Contains("ONCE-ONLY")));
    }

    [Fact]
    public void Whole_file_parent_at_start_does_not_hide_fragment_chapters()
    {
        using var book = new EpubFixture(false,
            [("one.xhtml", "<h1 id='one'>One heading</h1><p>FIRST-BODY</p><h1 id='two'>Two heading</h1><p>SECOND-BODY</p>")], []);
        book.Replace("OPS/nav.xhtml", """
            <!DOCTYPE html><html xmlns='http://www.w3.org/1999/xhtml' xmlns:epub='http://www.idpf.org/2007/ops'>
            <body><nav epub:type='toc'><ol><li><a href='one.xhtml'>Part One</a><ol>
            <li><a href='one.xhtml#one'>First chapter</a></li><li><a href='one.xhtml#two'>Second chapter</a></li>
            </ol></li></ol></nav></body></html>
            """);

        var result = DocumentParser.ParseEpub(book.Path);

        Assert.Equal(["First chapter", "Second chapter"], result.Sections.Select(s => s.Title));
        Assert.Equal(["OPS/one.xhtml#one", "OPS/one.xhtml#two"], result.Sections.Select(s => s.Locator));
        Assert.Equal(1, result.Sections.Count(s => s.Text.Contains("FIRST-BODY")));
    }

    [Fact]
    public void Missing_anchor_keeps_all_text_and_uses_real_heading_with_warning()
    {
        using var book = new EpubFixture(false,
            [("one.xhtml", "<h1>Actual heading</h1><p>ALL-THE-TEXT</p>")],
            [("one.xhtml#missing", "Unusable TOC label")]);

        var result = DocumentParser.ParseEpub(book.Path);

        var section = Assert.Single(result.Sections);
        Assert.Equal("Actual heading", section.Title);
        Assert.Equal("OPS/one.xhtml", section.Locator);
        Assert.Contains("ALL-THE-TEXT", section.Text);
        Assert.Contains("anchors were missing", result.Warning);
    }

    [Fact]
    public void Malformed_content_retains_whole_file_and_reports_fallback()
    {
        using var book = new EpubFixture(false,
            [("one.xhtml", "<h1 id='one'>One</h1><p>FIRST<p>SECOND<h1 id='two'>Two</h1><p>LAST")],
            [("one.xhtml#one", "One"), ("one.xhtml#two", "Two")]);

        var result = DocumentParser.ParseEpub(book.Path);

        var section = Assert.Single(result.Sections);
        Assert.Equal("OPS/one.xhtml", section.Locator);
        Assert.Contains("FIRST", section.Text);
        Assert.Contains("LAST", section.Text);
        Assert.Contains("markup could not be parsed", result.Warning);
    }

    [Fact]
    public void Empty_spine_files_and_empty_toc_slices_do_not_become_numbered_sections()
    {
        using var book = new EpubFixture(false,
            [("cover.xhtml", "<img src='cover.jpg'/>"),
             ("one.xhtml", "<a id='blank'/><a id='start'/><h1>Only chapter</h1><p>ACTUAL-TEXT</p><a id='end'/>")],
            [("cover.xhtml", "Cover"), ("one.xhtml#blank", "Only chapter"), ("one.xhtml#start", "Only chapter"), ("one.xhtml#end", "Empty end")]);

        var result = DocumentParser.ParseEpub(book.Path);

        var section = Assert.Single(result.Sections);
        Assert.Equal("Only chapter", section.Title);
        Assert.Equal("OPS/one.xhtml", section.Locator);
        Assert.Contains("ACTUAL-TEXT", section.Text);
    }

    [Fact]
    public void Image_only_book_has_honest_warning_and_no_false_readable_sections()
    {
        using var book = new EpubFixture(false, [("cover.xhtml", "<img src='cover.jpg'/>")], [("cover.xhtml", "Cover")]);

        var result = DocumentParser.ParseEpub(book.Path);

        Assert.Empty(result.Sections);
        Assert.Contains("No extractable text", result.Warning);
        Assert.Contains("original file", result.Warning);
    }

    [Fact]
    public void Unsplit_file_keeps_existing_locator_and_heading_or_honest_filename_fallback()
    {
        using var book = new EpubFixture(false,
            [("one.xhtml", "<h1 id='one'>Actual heading</h1><p>FIRST-TEXT</p>"), ("two.xhtml", "<p>SECOND-TEXT</p>")],
            [("one.xhtml#one", "Actual TOC title")]);

        var result = DocumentParser.ParseEpub(book.Path);

        Assert.Equal(["OPS/one.xhtml", "OPS/two.xhtml"], result.Sections.Select(s => s.Locator));
        Assert.Equal(["Actual TOC title", "Text from two.xhtml"], result.Sections.Select(s => s.Title));
    }

    [Fact]
    public void External_entities_never_read_local_files()
    {
        using var book = new EpubFixture(false, [("one.xhtml", "<p>SAFE-TEXT</p>")], []);
        var secret = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(secret, "PRIVATE-ENTITY-CONTENT");
            book.Replace("OPS/one.xhtml", $"<!DOCTYPE html [<!ENTITY external SYSTEM '{new Uri(secret).AbsoluteUri}'>]><html><body><h1>Safe</h1><p>SAFE-TEXT &external;</p></body></html>");

            var result = DocumentParser.ParseEpub(book.Path);

            Assert.Contains("SAFE-TEXT", Assert.Single(result.Sections).Text);
            Assert.DoesNotContain("PRIVATE-ENTITY-CONTENT", string.Join("", result.Sections.Select(s => s.Text)));
        }
        finally { File.Delete(secret); }
    }

    [Fact]
    public void Invalid_external_toc_links_are_ignored_with_warning_without_losing_spine_text()
    {
        using var book = new EpubFixture(false, [("one.xhtml", "<h1>Local heading</h1><p>LOCAL-TEXT</p>")],
            [("https://example.invalid/foreign#chapter", "External")]);

        var result = DocumentParser.ParseEpub(book.Path);

        Assert.Contains("LOCAL-TEXT", Assert.Single(result.Sections).Text);
        Assert.Contains("links are unavailable", result.Warning);
    }

    [Theory]
    [InlineData(false, "http://[")]
    [InlineData(true, "http://[")]
    [InlineData(false, "https://example.invalid:invalid/chapter")]
    [InlineData(true, "https://example.invalid:invalid/chapter")]
    public void Malformed_toc_reference_does_not_block_later_valid_chapters(bool ncx, string malformed)
    {
        using var book = new EpubFixture(ncx,
            [("one.xhtml", "<h1 id='one'>One</h1><p>FIRST-TEXT</p><h1 id='two'>Two</h1><p>SECOND-TEXT</p>")],
            [(malformed, "Broken reference"), ("one.xhtml#one", "First chapter"), ("one.xhtml#two", "Second chapter")]);

        var result = DocumentParser.ParseEpub(book.Path);

        Assert.Equal(["First chapter", "Second chapter"], result.Sections.Select(s => s.Title));
        Assert.Contains("FIRST-TEXT", result.Sections[0].Text);
        Assert.Contains("SECOND-TEXT", result.Sections[1].Text);
        Assert.Contains("links are unavailable", result.Warning);
    }

    [Theory]
    [InlineData(false, "http://[")]
    [InlineData(true, "http://[")]
    [InlineData(false, "https://example.invalid/nav.xhtml")]
    [InlineData(true, "https://example.invalid/toc.ncx")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public void Invalid_optional_navigation_manifest_path_keeps_readable_spine(bool ncx, string? reference)
    {
        using var book = new EpubFixture(ncx, [("one.xhtml", "<h1>Actual heading</h1><p>ALL-TEXT</p>")], []);
        var href = reference == null ? "" : $"href='{reference}'";
        var attributes = ncx ? "media-type='application/x-dtbncx+xml'" : "media-type='application/xhtml+xml' properties='nav'";
        book.Replace("OPS/book.opf", $"<package><manifest><item id='one' href='one.xhtml' media-type='application/xhtml+xml'/><item id='toc' {href} {attributes}/></manifest><spine><itemref idref='one'/></spine></package>");

        var result = DocumentParser.ParseEpub(book.Path);

        var section = Assert.Single(result.Sections);
        Assert.Equal("Actual heading", section.Title);
        Assert.Contains("ALL-TEXT", section.Text);
        Assert.Contains("could not be read", result.Warning);
    }

    [Fact]
    public void Encoded_hash_filenames_remain_distinct_from_fragment_locators()
    {
        using var book = new EpubFixture(false,
            [("one.xhtml", "<h1 id='chapter.xhtml'>First file</h1><p>FIRST-FILE</p><h1 id='last'>Last</h1><p>FIRST-LAST</p>"),
             ("one.xhtml#chapter.xhtml", "<h1>Hash filename</h1><p>HASH-FILE</p>"),
             ("one.xhtml%23chapter.xhtml", "<h1>Percent filename</h1><p>PERCENT-FILE</p>")],
            [("one.xhtml#chapter.xhtml", "Fragment chapter"), ("one.xhtml#last", "Last chapter"),
             ("one.xhtml%23chapter.xhtml", "Hash title"), ("one.xhtml%2523chapter.xhtml", "Percent title")]);

        var result = DocumentParser.ParseEpub(book.Path);

        Assert.Null(result.Warning);
        Assert.Equal(["Fragment chapter", "Last chapter", "Hash title", "Percent title"], result.Sections.Select(s => s.Title));
        Assert.Equal(["OPS/one.xhtml#chapter.xhtml", "OPS/one.xhtml#last", "OPS/one.xhtml%23chapter.xhtml", "OPS/one.xhtml%2523chapter.xhtml"], result.Sections.Select(s => s.Locator));
        Assert.Equal(result.Sections.Count, result.Sections.Select(s => s.Locator).Distinct().Count());
        Assert.Contains("HASH-FILE", result.Sections[2].Text);
        Assert.DoesNotContain("PERCENT-FILE", result.Sections[2].Text);
        Assert.Contains("PERCENT-FILE", result.Sections[3].Text);
    }

    private sealed class EpubFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".epub");

        public EpubFixture(bool ncx, (string Name, string Body)[] files, (string Href, string Label)[] links, bool prefixed = false)
        {
            using var archive = ZipFile.Open(Path, ZipArchiveMode.Create);
            void Entry(string name, string text)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.NoCompression).Open());
                writer.Write(text);
            }
            Entry("mimetype", "application/epub+zip");
            Entry("META-INF/container.xml", "<container><rootfiles><rootfile full-path='OPS/book.opf'/></rootfiles></container>");
            var manifest = string.Join("", files.Select((f, i) => $"<item id='i{i}' href='{string.Join('/', f.Name.Split('/').Select(Uri.EscapeDataString))}' media-type='application/xhtml+xml'/>"));
            manifest += ncx ? "<item id='toc' href='toc.ncx' media-type='application/x-dtbncx+xml'/>" : "<item id='toc' href='nav.xhtml' media-type='application/xhtml+xml' properties='nav'/>";
            var spine = string.Join("", files.Select((_, i) => $"<itemref idref='i{i}'/>"));
            Entry("OPS/book.opf", $"<package><metadata><title>TEST FIXTURE</title></metadata><manifest>{manifest}</manifest><spine>{spine}</spine></package>");
            static string Escape(string text) => System.Security.SecurityElement.Escape(text)!;
            if (ncx)
                Entry("OPS/toc.ncx", "<!DOCTYPE ncx PUBLIC '-//NISO//DTD ncx 2005-1//EN' 'http://www.daisy.org/z3986/2005/ncx-2005-1.dtd'><ncx xmlns='http://www.daisy.org/z3986/2005/ncx/'><navMap>" +
                    string.Join("", links.Select((l, i) => $"<navPoint id='n{i}'><navLabel><text>{Escape(l.Label)}</text></navLabel><content src='{Escape(l.Href)}'/></navPoint>")) + "</navMap></ncx>");
            else
                Entry("OPS/nav.xhtml", "<!DOCTYPE html><html xmlns='http://www.w3.org/1999/xhtml' xmlns:epub='http://www.idpf.org/2007/ops'><body><nav epub:type='toc'><ol>" +
                    string.Join("", links.Select(l => $"<li><a href='{Escape(l.Href)}'>{Escape(l.Label)}</a></li>")) + "</ol></nav></body></html>");
            foreach (var file in files)
                Entry("OPS/" + file.Name, prefixed ?
                    $"<!DOCTYPE html PUBLIC '-//W3C//DTD XHTML 1.1//EN' 'http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd'><x:html xmlns:x='http://www.w3.org/1999/xhtml'><x:head><x:title>PRIVATE-HEAD</x:title></x:head><x:body>{file.Body}</x:body></x:html>" :
                    $"<!DOCTYPE html><html xmlns='http://www.w3.org/1999/xhtml'><head><title>PRIVATE-HEAD</title></head><body>{file.Body}</body></html>");
        }

        public void Replace(string name, string content)
        {
            using var archive = ZipFile.Open(Path, ZipArchiveMode.Update);
            archive.GetEntry(name)?.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.NoCompression).Open());
            writer.Write(content);
        }

        public void Dispose() => File.Delete(Path);
    }
}
