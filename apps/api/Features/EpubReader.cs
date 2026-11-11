using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace RabbitHole.Features;

/// <summary>Reads bounded original EPUB resources. Nothing is extracted to disk or fetched remotely.</summary>
public sealed class EpubReader : IDisposable
{
    private const int MaxEntryBytes = 15 * 1024 * 1024;
    private static readonly XNamespace Html = "http://www.w3.org/1999/xhtml";
    private static readonly HashSet<string> HtmlTags = new("html head title body main article section nav aside header footer div span p h1 h2 h3 h4 h5 h6 a img picture source figure figcaption br hr ul ol li dl dt dd table caption colgroup col thead tbody tfoot tr th td blockquote pre code em strong b i u s small sub sup q cite abbr dfn time mark ruby rt rp bdi bdo details summary link style".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SvgTags = new("svg g path rect circle ellipse line polyline polygon text tspan defs symbol use image clipPath mask linearGradient radialGradient stop pattern title desc".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MathTags = new("math mrow mi mn mo mtext mspace ms mfrac msqrt mroot mstyle merror mpadded mphantom mfenced menclose msub msup msubsup munder mover munderover mmultiscripts mprescripts none mtable mtr mtd semantics annotation".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Attributes = new("id class title lang dir style alt width height colspan rowspan scope headers start reversed value type datetime open name role aria-label aria-hidden viewBox preserveAspectRatio d x y x1 y1 x2 y2 dx dy cx cy r rx ry points transform fill fill-rule fill-opacity stroke stroke-width stroke-linecap stroke-linejoin stroke-dasharray stroke-opacity opacity offset stop-color stop-opacity gradientUnits gradientTransform patternUnits patternTransform clip-path mask font-size text-anchor display xmlns".Split(' '), StringComparer.OrdinalIgnoreCase);
    private readonly ZipArchive archive;
    private readonly Dictionary<string, ZipArchiveEntry> entries;
    private readonly Dictionary<string, string> media = new(StringComparer.Ordinal);
    public List<string> Spine { get; } = [];

    public EpubReader(string file)
    {
        archive = ZipFile.OpenRead(file);
        try
        {
            entries = ValidateArchive(archive);
            if (ReadText("mimetype").Trim() != "application/epub+zip" || entries.ContainsKey("META-INF/encryption.xml"))
                throw new InvalidDataException("Encrypted or invalid EPUB.");
            var container = DocumentParser.Xml(ReadText("META-INF/container.xml"));
            var packagePath = container.Descendants().FirstOrDefault(x => x.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value
                ?? throw new InvalidDataException("Missing package.");
            var package = DocumentParser.Xml(ReadText(packagePath));
            var byId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in package.Descendants().Where(x => x.Name.LocalName == "item"))
            {
                var id = item.Attribute("id")?.Value;
                var href = item.Attribute("href")?.Value;
                if (id == null || href == null) continue;
                try
                {
                    var path = DocumentParser.ResolveEpubReference(packagePath, href).Path;
                    if (!entries.ContainsKey(path)) continue;
                    byId.TryAdd(id, path);
                    media.TryAdd(path, item.Attribute("media-type")?.Value ?? "");
                }
                catch (InvalidDataException) { /* Remote publication resources are not fetched. */ }
            }
            foreach (var item in package.Descendants().Where(x => x.Name.LocalName == "itemref"))
                if (byId.TryGetValue(item.Attribute("idref")?.Value ?? "", out var path) && IsChapter(path) && !Spine.Contains(path)) Spine.Add(path);
            if (Spine.Count == 0 || Spine.Count > 1500) throw new InvalidDataException("No supported spine.");
        }
        catch { archive.Dispose(); throw; }
    }

    internal static Dictionary<string, ZipArchiveEntry> ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > 4000) throw new InvalidDataException("Too many archive entries.");
        long expanded = 0;
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.StartsWith('/') || name.Contains('\\') || name.Split('/').Any(x => x is ".." or ".") || name.Contains(':') || name.Any(char.IsControl))
                throw new InvalidDataException("Unsafe archive entry.");
            if (entry.Length > MaxEntryBytes || (expanded += entry.Length) > DocumentParser.MaxExpandedBytes ||
                entry.Length > 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 150)
                throw new InvalidDataException("Archive expansion limit exceeded.");
            if (!entries.TryAdd(name, entry)) throw new InvalidDataException("Ambiguous archive paths.");
        }
        return entries;
    }

    public bool IsChapter(string path) => media.GetValueOrDefault(path) is "application/xhtml+xml" or "text/html";
    public IEnumerable<string> Chapters => Spine.Concat(media.Keys.Where(path => IsChapter(path) && !Spine.Contains(path)));
    public static string AssetUrl(Guid id, string path) => $"/api/library/books/{id}/epub/assets/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    internal static DocumentParser.EpubReference? Locator(string? locator)
    {
        if (string.IsNullOrEmpty(locator) || locator == "original") return null;
        if (locator.StartsWith("epub-prefix:", StringComparison.Ordinal)) locator = locator[12..];
        try { return DocumentParser.ResolveEpubReference("", locator); }
        catch (InvalidDataException) { return null; }
    }

    private byte[] ReadBytes(string path)
    {
        if (!entries.TryGetValue(path, out var entry)) throw new FileNotFoundException();
        using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            if (output.Length + count > MaxEntryBytes || output.Length + count > entry.Length) throw new InvalidDataException("Expansion limit exceeded.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private string ReadText(string path)
    {
        using var input = new MemoryStream(ReadBytes(path));
        using var reader = new StreamReader(input, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    public (byte[] Bytes, string Mime)? Resource(Guid id, string path)
    {
        if (!media.TryGetValue(path, out var mime)) return null;
        if (IsChapter(path) || mime == "image/svg+xml")
        {
            var doc = DocumentParser.Xml(ReadText(path));
            if (doc.Root == null || IsChapter(path) && !doc.Root.Name.LocalName.Equals("html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid chapter.");
            Sanitize(doc.Root, id, path);
            if (IsChapter(path))
            {
                doc.Root.SetAttributeValue("data-epub-document", "true");
                var head = doc.Root.Elements().FirstOrDefault(x => x.Name.LocalName == "head");
                if (head == null) { head = new XElement(Html + "head"); doc.Root.AddFirst(head); }
                // Defaults precede publisher styles; the original can still specify its own layout.
                head.AddFirst(new XElement(Html + "style", "html{background:#fff;color:#222}body{margin:24px;font-family:Georgia,serif;line-height:1.65}img,svg{max-width:100%;height:auto}html{overflow-wrap:break-word}"));
            }
            return (Encoding.UTF8.GetBytes(doc.Root.ToString(SaveOptions.DisableFormatting)), IsChapter(path) ? "application/xhtml+xml; charset=utf-8" : mime);
        }
        if (mime is "text/css" or "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "image/avif" or
            "font/woff" or "font/woff2" or "font/ttf" or "font/otf" or "application/font-woff" or "application/vnd.ms-opentype" or "application/x-font-ttf" or "application/x-font-opentype")
            return (ReadBytes(path), mime);
        return null;
    }

    private void Sanitize(XElement root, Guid id, string path)
    {
        foreach (var element in root.DescendantsAndSelf().ToList())
        {
            var tag = element.Name.LocalName;
            var ns = element.Name.NamespaceName;
            var allowed = ns is "" or "http://www.w3.org/1999/xhtml" ? HtmlTags.Contains(tag)
                : ns == "http://www.w3.org/2000/svg" ? SvgTags.Contains(tag)
                : ns == "http://www.w3.org/1998/Math/MathML" && MathTags.Contains(tag);
            if (!allowed) { if (element == root) throw new InvalidDataException("Unsupported document root."); element.Remove(); continue; }
            if (ns == "") element.Name = Html + tag.ToLowerInvariant();
            if (tag.Equals("link", StringComparison.OrdinalIgnoreCase) && element.Attribute("rel")?.Value != "stylesheet") { element.Remove(); continue; }
            foreach (var attribute in element.Attributes().ToList())
            {
                var name = attribute.Name.LocalName;
                if (attribute.IsNamespaceDeclaration) continue;
                if (name is "href" or "src")
                {
                    if (tag is not ("a" or "img" or "image" or "use" or "link")) { attribute.Remove(); continue; }
                    try
                    {
                        var target = DocumentParser.ResolveEpubReference(path, attribute.Value);
                        if (!media.ContainsKey(target.Path)) { attribute.Remove(); continue; }
                        attribute.Value = AssetUrl(id, target.Path) + (target.Fragment == null ? "" : "#" + Uri.EscapeDataString(target.Fragment));
                    }
                    catch (InvalidDataException)
                    {
                        // External links remain deliberate reader actions; no resource can auto-fetch them.
                        if (tag == "a" && Uri.TryCreate(attribute.Value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https" && url.UserInfo.Length == 0)
                        {
                            element.SetAttributeValue("data-epub-external", url.AbsoluteUri);
                            attribute.Value = "#";
                        }
                        else attribute.Remove();
                    }
                }
                else if (tag == "link" && name == "rel") { /* Only stylesheets survive above. */ }
                else if (!Attributes.Contains(name) || attribute.Name.NamespaceName.Length > 0 && attribute.Name != XNamespace.Xml + "lang") attribute.Remove();
            }
        }
        // A publisher's processing instructions/doctype must not introduce styles or entities.
        foreach (var instruction in root.DescendantNodes().OfType<XProcessingInstruction>().ToList()) instruction.Remove();
    }

    public void Dispose() => archive.Dispose();
}
