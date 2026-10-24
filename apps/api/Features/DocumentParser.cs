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
        var labels = new Dictionary<string, EpubTocLabel>(StringComparer.Ordinal);
        var navTargets = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new HashSet<string>();
        void AddLabel(string parent, string reference, string label, int depth, bool keepExisting = false)
        {
            try
            {
                var target = ResolveEpubReference(parent, reference);
                if (keepExisting && navTargets.Contains(target.Locator)) return;
                var text = PlainText(label);
                if (text.Length == 0) return;
                if (!labels.TryGetValue(target.Locator, out var existing) || depth > existing.Depth)
                    labels[target.Locator] = new(target.Path, target.Fragment, text, depth);
            }
            catch (InvalidDataException)
            {
                warnings.Add("Some table of contents links are unavailable; readable spine text has been retained.");
            }
        }
        var nav = manifest.Values.FirstOrDefault(x => (x.Attribute("properties")?.Value ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("nav"));
        if (nav != null)
        {
            try
            {
                var navPath = Resolve(packagePath, nav.Attribute("href")?.Value ?? throw new InvalidDataException("Missing navigation path."));
                var navXml = Xml(Read(navPath));
                var toc = navXml.Descendants().FirstOrDefault(x => x.Name.LocalName == "nav" && x.Attributes().Any(a =>
                    a.Name.LocalName == "type" && a.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("toc")));
                foreach (var link in (toc?.Descendants() ?? []).Where(x => x.Name.LocalName == "a" && x.Attribute("href") != null))
                    AddLabel(navPath, link.Attribute("href")!.Value, link.Value, link.Ancestors().Count(x => x.Name.LocalName == "li"));
            }
            catch (Exception e) when (e is XmlException or InvalidDataException)
            {
                warnings.Add("The EPUB navigation document could not be read; sections use the available headings and spine order.");
            }
        }
        navTargets.UnionWith(labels.Keys);
        var ncx = manifest.Values.FirstOrDefault(x => x.Attribute("media-type")?.Value == "application/x-dtbncx+xml");
        if (ncx != null)
        {
            try
            {
                var ncxPath = Resolve(packagePath, ncx.Attribute("href")?.Value ?? throw new InvalidDataException("Missing NCX path."));
                foreach (var point in Xml(Read(ncxPath)).Descendants().Where(x => x.Name.LocalName == "navPoint"))
                {
                    var src = point.Elements().FirstOrDefault(x => x.Name.LocalName == "content")?.Attribute("src")?.Value;
                    var label = point.Elements().FirstOrDefault(x => x.Name.LocalName == "navLabel")?.Value;
                    if (src != null && label != null)
                    {
                        // EPUB 3 navigation takes precedence over the EPUB 2 compatibility TOC.
                        AddLabel(ncxPath, src, label, point.Ancestors().Count(x => x.Name.LocalName == "navPoint"), nav != null);
                    }
                }
            }
            catch (Exception e) when (e is XmlException or InvalidDataException)
            {
                warnings.Add("The EPUB NCX table of contents could not be read; sections use the available headings and spine order.");
            }
        }
        var sections = new List<ParsedSection>();
        var length = 0;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spine in package.Descendants().Where(x => x.Name.LocalName == "itemref"))
        {
            if (!manifest.TryGetValue(spine.Attribute("idref")?.Value ?? "", out var item)) continue;
            if (item.Attribute("media-type")?.Value is not ("application/xhtml+xml" or "text/html")) continue;
            var location = Resolve(packagePath, item.Attribute("href")!.Value);
            var fileLocator = new EpubReference(location, null).Locator;
            if (!visited.Add(location)) continue;
            var raw = Read(location);
            EpubText document;
            try { document = ExtractEpubText(Xml(raw)); }
            catch (XmlException)
            {
                // Malformed legacy HTML remains readable, but unreliable anchors must not split or discard it.
                var fallback = PlainText(EpubDoctypeRegex().Replace(raw, ""));
                document = new(fallback, new(StringComparer.Ordinal), []);
                warnings.Add("Some EPUB markup could not be parsed; that file's text is retained as a whole section.");
            }
            length += document.Text.Length;
            if (length > MaxTextChars || sections.Count >= 1500) throw new InvalidDataException("Book text exceeds limit.");
            var boundaries = new List<(int Offset, EpubTocLabel Label)>();
            foreach (var label in labels.Values.Where(x => x.Path == location))
            {
                if (label.Fragment == null) boundaries.Add((0, label));
                else if (document.Anchors.TryGetValue(label.Fragment, out var offset)) boundaries.Add((offset, label));
                else warnings.Add("Some table of contents anchors were missing; readable spine text has been retained.");
            }
            // Alias anchors and nested part/chapter entries can point to the same visible start.
            var starts = boundaries.GroupBy(x => SkipEpubWhitespace(document.Text, x.Offset))
                .Select(g => (Offset: g.Key, Label: g.OrderByDescending(x => x.Label.Depth).First().Label))
                .OrderBy(x => x.Offset).ToList();
            if (starts.Count == 0 || !string.IsNullOrWhiteSpace(document.Text[..starts[0].Offset]))
                starts.Insert(0, (0, new(location, null, "", -1)));
            var fileSections = new List<ParsedSection>();
            for (var i = 0; i < starts.Count; i++)
            {
                var start = starts[i];
                var end = i + 1 < starts.Count ? starts[i + 1].Offset : document.Text.Length;
                var text = document.Text[start.Offset..end].Trim();
                if (text.Length == 0) continue;
                var heading = document.Headings.FirstOrDefault(x => x.Offset >= start.Offset && x.Offset < end).Title;
                var name = start.Label.Title.Length > 0 ? start.Label.Title : heading ??
                    (starts.Count > 1 && i == 0 ? "Front matter" : $"Text from {Path.GetFileName(location)}");
                var locator = new EpubReference(location, start.Label.Fragment).Locator;
                fileSections.Add(new(name, text, locator));
            }
            // Keep existing locators for files that still form a single readable section.
            if (fileSections.Count == 1) fileSections[0] = fileSections[0] with { Locator = fileLocator };
            else
                for (var i = 0; i < fileSections.Count; i++)
                    if (fileSections[i].Locator == fileLocator)
                        fileSections[i] = fileSections[i] with { Locator = $"epub-prefix:{fileLocator}" };
            sections.AddRange(fileSections);
            if (sections.Count > 1500) throw new InvalidDataException("Book text exceeds limit.");
        }
        if (sections.Count == 0)
            warnings.Add("No extractable text was found. This EPUB may contain only images; use the original file.");
        return new(title, author, 0, sections, warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    private sealed record EpubTocLabel(string Path, string? Fragment, string Title, int Depth);
    private sealed record EpubText(string Text, Dictionary<string, int> Anchors, List<(int Offset, string Title)> Headings);
    private sealed record EpubReference(string Path, string? Fragment)
    {
        // Distinguish a literal '#' in an archive filename from a chapter fragment separator.
        // Ordinary existing locators stay unchanged; '%' must escape too to avoid a second alias.
        private string LocatorPath => Path.Replace("%", "%25").Replace("#", "%23");
        public string Locator => Fragment == null ? LocatorPath : $"{LocatorPath}#{Fragment}";
    }

    private static int SkipEpubWhitespace(string text, int offset)
    {
        while (offset < text.Length && char.IsWhiteSpace(text[offset])) offset++;
        return offset;
    }

    private static EpubText ExtractEpubText(XDocument document)
    {
        var body = document.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("body", StringComparison.OrdinalIgnoreCase))
            ?? document.Root ?? throw new XmlException("Missing content root.");
        var text = new StringBuilder();
        var anchors = new Dictionary<string, int>(StringComparer.Ordinal);
        var headings = new List<(int Offset, string Title)>();
        void Break()
        {
            if (text.Length > 0 && text[^1] != '\n') text.Append('\n');
        }
        void Visit(XElement element)
        {
            var tag = element.Name.LocalName.ToLowerInvariant();
            if (tag is "script" or "style" or "head" or "template") return;
            var block = tag is "p" or "div" or "section" or "article" or "li" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "blockquote" or "tr" or "br" or "hr";
            if (block) Break();
            foreach (var attribute in element.Attributes().Where(a => a.Name.LocalName == "id" || tag == "a" && a.Name.LocalName == "name"))
                anchors.TryAdd(attribute.Value, text.Length);
            var start = text.Length;
            foreach (var node in element.Nodes())
            {
                if (node is XElement child) Visit(child);
                else if (node is XText value) text.Append(value.Value);
            }
            if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                var heading = SpaceRegex().Replace(text.ToString(start, text.Length - start), " ").Trim();
                if (heading.Length > 0) headings.Add((start, heading));
            }
            if (block) Break();
        }
        Visit(body);
        return new(text.ToString(), anchors, headings);
    }

    private static string Resolve(string parent, string relative) => ResolveEpubReference(parent, relative).Path;

    private static EpubReference ResolveEpubReference(string parent, string relative)
    {
        var root = new Uri("https://epub.invalid/");
        // parent is an already decoded ZIP entry name, not a URL. Re-encode its components
        // so literal '#'/'?'/'%' characters cannot change which archive directory is used.
        var parentUri = new Uri(root, string.Join('/', parent.Split('/').Select(Uri.EscapeDataString)));
        if (!Uri.TryCreate(parentUri, relative, out var resolved))
            throw new InvalidDataException("Invalid EPUB resource reference.");
        if (resolved.Host != root.Host || resolved.Scheme != "https" || resolved.Query.Length > 0 || resolved.Port != 443 || resolved.UserInfo.Length > 0)
            throw new InvalidDataException("External EPUB resources are not fetched.");
        var path = Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/'));
        if (path.Contains('\\') || path.Split('/').Any(x => x == "..") || path.Contains(':'))
            throw new InvalidDataException("Unsafe EPUB resource path.");
        return new(path, resolved.Fragment.Length > 1 ? Uri.UnescapeDataString(resolved.Fragment[1..]) : null);
    }

    private static XDocument Xml(string content)
    {
        // XHTML EPUBs commonly rely on HTML entities declared by an external DTD. Decode the
        // known character references locally, without retrieving that DTD or allowing its entities.
        content = EpubHtmlEntityRegex().Replace(content, match =>
        {
            var decoded = WebUtility.HtmlDecode(match.Value);
            return decoded == match.Value ? match.Value : string.Concat(decoded.EnumerateRunes().Select(r => $"&#{r.Value};"));
        });
        using var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Parse, XmlResolver = null, MaxCharactersFromEntities = 4096, MaxCharactersInDocument = 15 * 1024 * 1024 });
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
    [GeneratedRegex(@"<!DOCTYPE[^>\[]*(?:\[[\s\S]*?\]\s*)?>", RegexOptions.IgnoreCase)] private static partial Regex EpubDoctypeRegex();
    [GeneratedRegex(@"&[A-Za-z][A-Za-z0-9]+;")] private static partial Regex EpubHtmlEntityRegex();
}
