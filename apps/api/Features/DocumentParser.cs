using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace RabbitHole.Features;

public record ParsedSection(string Title, string Text, string Locator, int? StartPage = null, int? EndPage = null);
public record ParsedDocument(string? Title, string? Author, int PageCount, List<ParsedSection> Sections, string? Warning = null);

/// <summary>Invoked in a deadline-limited child process; never executes embedded content.</summary>
public static partial class DocumentParser
{
    public const long MaxExpandedBytes = 100 * 1024 * 1024;
    public const int MaxTextChars = 12 * 1024 * 1024;

    public static async Task RunWorkerAsync(string path, string kind, string output)
    {
        try
        {
            var parsed = kind == "pdf" ? ParsePdf(path) : ParseEpub(path);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(parsed));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Error details from third party files must not escape into logs or HTTP responses.
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new ParsedDocument(null, null, 0, [],
                "Text extraction failed. The original file remains available. Encrypted/DRM files are not supported.")));
        }
    }

    public static ParsedDocument ParsePdf(string path)
    {
        using var pdf = PdfDocument.Open(path);
        if (pdf.NumberOfPages > 2000) throw new InvalidDataException("PDF exceeds 2,000 pages.");
        var sections = new List<ParsedSection>();
        var total = 0;
        var until = DateTime.UtcNow.AddSeconds(25);
        foreach (var page in pdf.GetPages())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException();
            var text = ContentOrderTextExtractor.GetText(page);
            total += text.Length;
            if (total > MaxTextChars) throw new InvalidDataException("Too much extracted text.");
            sections.Add(new($"Page {page.Number}", text, $"page={page.Number}", page.Number, page.Number));
        }
        // Page records always remain available. Real PDF bookmarks add chapter ranges if present.
        if (pdf.TryGetBookmarks(out var bookmarks))
        {
            var ranges = new List<(string Title, int Page)>();
            void Visit(IEnumerable<UglyToad.PdfPig.Outline.BookmarkNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node is UglyToad.PdfPig.Outline.DocumentBookmarkNode destination &&
                        destination.Destination.PageNumber is > 0 && destination.Destination.PageNumber <= pdf.NumberOfPages)
                        ranges.Add((node.Title, destination.Destination.PageNumber));
                    Visit(node.Children);
                }
            }
            Visit(bookmarks.Roots);
            var ordered = ranges.DistinctBy(x => x.Page).OrderBy(x => x.Page).Take(500).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var start = ordered[i].Page;
                var end = i + 1 < ordered.Count ? ordered[i + 1].Page - 1 : pdf.NumberOfPages;
                var text = string.Join("\n\n", sections.Where(s => s.StartPage >= start && s.EndPage <= end).Select(s => s.Text));
                total += text.Length;
                if (total > MaxTextChars) break;
                sections.Add(new(ordered[i].Title, text, $"bookmark:{start}-{end}", start, end));
            }
        }
        return new(pdf.Information.Title, pdf.Information.Author, pdf.NumberOfPages, sections,
            sections.All(x => string.IsNullOrWhiteSpace(x.Text)) ? "No extractable text. This may be a scanned PDF; OCR is not configured. Use the original file." : null);
    }

    public static ParsedDocument ParseEpub(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 4000) throw new InvalidDataException("Too many archive entries.");
        long expanded = 0;
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.StartsWith('/') || name.Contains('\\') || name.Split('/').Any(x => x == "..") || name.Contains(':'))
                throw new InvalidDataException("Unsafe archive entry.");
            if (entry.Length > 15 * 1024 * 1024 || (expanded += entry.Length) > MaxExpandedBytes ||
                entry.Length > 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 150)
                throw new InvalidDataException("Archive expansion limit exceeded.");
            if (!entries.TryAdd(name, entry)) throw new InvalidDataException("Ambiguous archive paths.");
        }
        string Read(string name)
        {
            if (!entries.TryGetValue(name, out var entry)) throw new InvalidDataException("Missing EPUB entry.");
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            var text = reader.ReadToEnd();
            if (text.Length > 15 * 1024 * 1024) throw new InvalidDataException("Entry too large.");
            return text;
        }
        if (Read("mimetype").Trim() != "application/epub+zip") throw new InvalidDataException("Not an EPUB.");
        if (entries.ContainsKey("META-INF/encryption.xml")) throw new InvalidDataException("Encrypted EPUB not supported.");
        var container = Xml(Read("META-INF/container.xml"));
        var packagePath = container.Descendants().FirstOrDefault(x => x.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value
            ?? throw new InvalidDataException("No package.");
        var package = Xml(Read(packagePath));
        var title = package.Descendants().FirstOrDefault(x => x.Name.LocalName == "title")?.Value;
        var author = package.Descendants().FirstOrDefault(x => x.Name.LocalName == "creator")?.Value;
        var manifest = package.Descendants().Where(x => x.Name.LocalName == "item" && x.Attribute("id") != null)
            .ToDictionary(x => x.Attribute("id")!.Value, x => x);
        var labels = new Dictionary<string, string>();
        var nav = manifest.Values.FirstOrDefault(x => (x.Attribute("properties")?.Value ?? "").Split(' ').Contains("nav"));
        if (nav != null)
        {
            var navPath = Resolve(packagePath, nav.Attribute("href")!.Value);
            var navXml = Xml(Read(navPath));
            var toc = navXml.Descendants().FirstOrDefault(x => x.Name.LocalName == "nav" && x.Attributes().Any(a => a.Name.LocalName == "type" && a.Value == "toc"));
            foreach (var link in (toc ?? navXml.Root!).Descendants().Where(x => x.Name.LocalName == "a" && x.Attribute("href") != null))
                labels.TryAdd(Resolve(navPath, link.Attribute("href")!.Value), PlainText(link.Value));
        }
        var ncx = manifest.Values.FirstOrDefault(x => x.Attribute("media-type")?.Value == "application/x-dtbncx+xml");
        if (ncx != null)
        {
            var ncxPath = Resolve(packagePath, ncx.Attribute("href")!.Value);
            foreach (var point in Xml(Read(ncxPath)).Descendants().Where(x => x.Name.LocalName == "navPoint"))
            {
                var src = point.Elements().FirstOrDefault(x => x.Name.LocalName == "content")?.Attribute("src")?.Value;
                var label = point.Elements().FirstOrDefault(x => x.Name.LocalName == "navLabel")?.Value;
                if (src != null && label != null) labels.TryAdd(Resolve(ncxPath, src), PlainText(label));
            }
        }
        var sections = new List<ParsedSection>();
        var length = 0;
        foreach (var spine in package.Descendants().Where(x => x.Name.LocalName == "itemref"))
        {
            if (!manifest.TryGetValue(spine.Attribute("idref")?.Value ?? "", out var item)) continue;
            if (item.Attribute("media-type")?.Value is not ("application/xhtml+xml" or "text/html")) continue;
            var location = Resolve(packagePath, item.Attribute("href")!.Value);
            var raw = Read(location);
            var text = PlainText(raw);
            length += text.Length;
            if (length > MaxTextChars || sections.Count >= 1500) throw new InvalidDataException("Book text exceeds limit.");
            var heading = HeadingRegex().Match(raw);
            var name = labels.GetValueOrDefault(location) ?? (heading.Success ? PlainText(heading.Groups[1].Value) : $"Section {sections.Count + 1}");
            sections.Add(new(name, text, location));
        }
        if (sections.Count == 0) throw new InvalidDataException("No readable EPUB spine entries.");
        return new(title, author, 0, sections);
    }

    private static string Resolve(string parent, string relative)
    {
        var root = new Uri("https://epub.invalid/");
        var resolved = new Uri(new Uri(root, parent), relative);
        if (resolved.Host != root.Host || resolved.Scheme != "https" || resolved.Query.Length > 0)
            throw new InvalidDataException("External EPUB resources are not fetched.");
        return Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
    }

    private static XDocument Xml(string content)
    {
        using var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 15 * 1024 * 1024 });
        return XDocument.Load(reader);
    }

    public static string PlainText(string content)
    {
        var clean = ScriptRegex().Replace(content, " ");
        clean = BreakRegex().Replace(clean, "\n");
        clean = TagRegex().Replace(clean, " ");
        clean = WebUtility.HtmlDecode(clean);
        return SpaceRegex().Replace(clean, " ").Trim();
    }

    [GeneratedRegex(@"<(script|style|head)\b[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase)] private static partial Regex ScriptRegex();
    [GeneratedRegex(@"</(?:p|div|h[1-6]|li|section)>|<br\s*/?>", RegexOptions.IgnoreCase)] private static partial Regex BreakRegex();
    [GeneratedRegex("<[^>]+>")] private static partial Regex TagRegex();
    [GeneratedRegex(@"[^\S\r\n]+")] private static partial Regex SpaceRegex();
    [GeneratedRegex(@"<h[1-3]\b[^>]*>([\s\S]*?)</h[1-3]>", RegexOptions.IgnoreCase)] private static partial Regex HeadingRegex();
}
