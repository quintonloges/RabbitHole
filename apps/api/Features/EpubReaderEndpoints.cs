using System.Xml;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;

namespace RabbitHole.Features;

public static class EpubReaderEndpoints
{
    public record Chapter(string Title, string Href);

    public static void MapEpubReaderEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/library/books/{id:guid}/epub", async (Guid id, AppDbContext db, DocumentStore store, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
            if (document == null) return Results.NotFound();
            if (document.MimeType != "application/epub+zip") return Results.BadRequest();
            try
            {
                using var book = new EpubReader(store.PathFor(document.StorageName));
                var sections = await db.Sections.AsNoTracking().Where(s => s.DocumentId == id).OrderBy(s => s.Order)
                    .Select(s => new { s.ItemId, s.Locator, s.IsSuperseded, Title = db.Items.Where(i => i.Id == s.ItemId).Select(i => i.Title).FirstOrDefault() ?? s.Title }).ToListAsync(ct);
                var targets = sections.Select(section => new { Section = section, Target = EpubReader.Locator(section.Locator) })
                    .Where(x => x.Target != null && book.IsChapter(x.Target.Path)).ToList();
                string Href(DocumentParser.EpubReference target) => EpubReader.AssetUrl(id, target.Path) + (target.Fragment == null ? "" : "#" + Uri.EscapeDataString(target.Fragment));
                var chapters = new List<Chapter>();
                foreach (var path in book.Chapters)
                {
                    var matches = targets.Where(x => x.Target!.Path == path && !x.Section.IsSuperseded).ToList();
                    if (matches.Count == 0) chapters.Add(new(Path.GetFileNameWithoutExtension(path), EpubReader.AssetUrl(id, path)));
                    else chapters.AddRange(matches.Select(x => new Chapter(x.Section.Title, Href(x.Target!))));
                }
                return Results.Ok(new { chapters, entryLocations = targets.ToDictionary(x => x.Section.ItemId, x => Href(x.Target!)) });
            }
            catch (Exception error) when (error is InvalidDataException or XmlException or IOException or ArgumentException)
            {
                return Unavailable(error);
            }
        });

        routes.MapGet("/api/library/books/{id:guid}/epub/assets/{**path}", async (Guid id, string path, AppDbContext db, DocumentStore store, HttpContext context, CancellationToken ct) =>
        {
            var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
            if (document == null) return Results.NotFound();
            if (document.MimeType != "application/epub+zip") return Results.BadRequest();
            try
            {
                using var book = new EpubReader(store.PathFor(document.StorageName));
                var resource = book.Resource(id, path);
                if (resource == null) return Results.NotFound();
                // Even directly opened chapters cannot execute scripts, submit forms or load remote content.
                context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'; sandbox allow-same-origin";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                return Results.Bytes(resource.Value.Bytes, resource.Value.Mime);
            }
            catch (Exception error) when (error is InvalidDataException or XmlException or IOException or ArgumentException)
            {
                return Unavailable(error);
            }
        });
    }

    private static IResult Unavailable(Exception error) => Results.Problem(error is FileNotFoundException
        ? "The original EPUB or one of its resources is missing. Restore the original file or use Extracted text."
        : "This EPUB cannot be displayed safely. Use Extracted text or download the original for an EPUB app. Encrypted books are not supported.",
        statusCode: error is FileNotFoundException ? 404 : 422);
}
