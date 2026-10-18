using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using RabbitHole.Data;
using RabbitHole.Domain;

namespace RabbitHole.Features;

public static class DocumentEndpoints
{
    private const long MaxUpload = 50 * 1024 * 1024;
    private static readonly SemaphoreSlim ImportGate = new(1, 1);
    public static IServiceCollection AddDocuments(this IServiceCollection services)
    {
        services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaxUpload + 1024 * 1024);
        services.AddScoped<DocumentStore>();
        return services;
    }

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/library/books", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Documents.AsNoTracking().ToListAsync(ct)).OrderByDescending(d => d.CreatedAt).Select(PublicDocument)));
        routes.MapPost("/api/library/books/upload", Upload).DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxUpload + 1024 * 1024));
        routes.MapPost("/api/library/books/{id:guid}/retry", Retry);
        routes.MapGet("/api/library/books/{id:guid}/sections", async (Guid id, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Sections.AsNoTracking().Where(s => s.DocumentId == id).OrderBy(s => s.Order).Select(s => new
            { s.Id, s.DocumentId, s.ItemId, s.Title, s.Order, s.StartPage, s.EndPage, s.Locator }).ToListAsync(ct)));
        routes.MapGet("/api/library/books/{id:guid}/file", async (Guid id, AppDbContext db, DocumentStore store, HttpContext context, CancellationToken ct) =>
        {
            var doc = await db.Documents.FindAsync([id], ct);
            if (doc == null) return Results.NotFound();
            var path = store.PathFor(doc.StorageName);
            if (!File.Exists(path)) return Results.Problem("The original file is missing. Restore the uploads volume from your backup.", statusCode: 404);
            context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(path, doc.MimeType, doc.MimeType == "application/pdf" && !context.Request.Query.ContainsKey("download") ? null : doc.FileName, enableRangeProcessing: true);
        });
        routes.MapPatch("/api/library/books/{id:guid}", async (Guid id, BookEdit request, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 500 || request.Author?.Length > 500)
                return Results.Problem("A title of 1–500 characters is required.", statusCode: 400);
            var doc = await db.Documents.FindAsync([id], ct);
            if (doc == null) return Results.NotFound();
            doc.Title = request.Title.Trim(); doc.Author = request.Author?.Trim();
            var items = await db.Items.Where(i => i.DocumentId == id).ToListAsync(ct);
            foreach (var item in items) { item.Author = doc.Author; item.UpdatedAt = DateTimeOffset.UtcNow; }
            await db.SaveChangesAsync(ct);
            return Results.Ok(PublicDocument(doc));
        });
        routes.MapPost("/api/library/books/{id:guid}/sections", async (Guid id, RangeEdit request, AppDbContext db, CancellationToken ct) =>
        {
            var doc = await db.Documents.FindAsync([id], ct);
            if (doc == null) return Results.NotFound();
            if (doc.MimeType != "application/pdf" || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 500 ||
                request.StartPage < 1 || request.EndPage < request.StartPage || request.EndPage > doc.PageCount)
                return Results.Problem("Choose a title and a valid page range within this PDF.", statusCode: 400);
            var pages = await db.Sections.Where(s => s.DocumentId == id && s.StartPage >= request.StartPage && s.EndPage <= request.EndPage).ToListAsync(ct);
            var originals = pages.Where(s => s.Locator?.StartsWith("page=", StringComparison.Ordinal) == true).OrderBy(s => s.StartPage).ToList();
            var text = string.Join("\n\n", originals.Select(s => s.Text));
            if (text.Length > DocumentParser.MaxTextChars) return Results.Problem("Page range is too large.", statusCode: 400);
            var order = (await db.Sections.Where(s => s.DocumentId == id).MaxAsync(s => (int?)s.Order, ct) ?? 0) + 1;
            var section = await AddSectionAsync(db, doc, new(request.Title.Trim(), text, $"manual:{request.StartPage}-{request.EndPage}", request.StartPage, request.EndPage), order, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(section);
        });
        return routes;
    }

    private static async Task<IResult> Upload(HttpRequest request, AppDbContext db, DocumentStore store, CancellationToken ct)
    {
        if (!request.HasFormContentType) return Results.Problem("Upload a PDF or EPUB using the file field.", statusCode: 400);
        if (!await ImportGate.WaitAsync(0, ct)) return Results.Problem("Another document is being imported. Please try again shortly.", statusCode: 429);
        string? path = null;
        var retained = false;
        try
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file == null || file.Length is < 5 or > MaxUpload)
                return Results.Problem("Choose a PDF or EPUB up to 50 MiB.", statusCode: 400);
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext is not (".pdf" or ".epub")) return Results.Problem("Only PDF and EPUB files are supported.", statusCode: 400);
            var mime = ext == ".pdf" ? "application/pdf" : "application/epub+zip";
            if (file.ContentType is not ("" or "application/octet-stream") && file.ContentType != mime && !(ext == ".epub" && file.ContentType == "application/zip"))
                return Results.Problem("The file type does not match its extension.", statusCode: 400);
            var name = Guid.NewGuid().ToString("N") + ext;
            path = store.PathFor(name);
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var source = file.OpenReadStream())
            {
                var buffer = new byte[65536]; long written = 0; int count;
                while ((count = await source.ReadAsync(buffer, ct)) > 0)
                {
                    written += count;
                    if (written > MaxUpload) return Results.Problem("The upload exceeds 50 MiB.", statusCode: 413);
                    await target.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            var signature = new byte[5];
            await using (var input = File.OpenRead(path)) await input.ReadExactlyAsync(signature, ct);
            if (ext == ".pdf" && !signature.AsSpan().SequenceEqual("%PDF-"u8) || ext == ".epub" && !(signature[0] == 0x50 && signature[1] == 0x4b && signature[2] == 3 && signature[3] == 4))
                return Results.Problem("The file signature does not match PDF or EPUB.", statusCode: 400);
            string hash;
            await using (var input = File.OpenRead(path)) hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
            var existing = await db.Documents.FirstOrDefaultAsync(d => d.Sha256 == hash, ct);
            if (existing != null)
            {
                var original = store.PathFor(existing.StorageName);
                var restored = !File.Exists(original);
                if (restored)
                {
                    // A verified matching upload can restore an absent original without replacing the document.
                    File.Move(path, original);
                    path = null;
                }
                if (restored || existing.Status is "parsing" or "pending" or "failed" or "original-only")
                    await ProcessDocumentAsync(db, store, existing, original, false, ct);
                return Results.Ok(new { document = PublicDocument(existing), sections = await db.Sections.Where(s => s.DocumentId == existing.Id).OrderBy(s => s.Order).ToListAsync(CancellationToken.None), duplicate = true });
            }
            var filename = Path.GetFileName(file.FileName.Replace('\\', '/'));
            var doc = new LibraryDocument
            {
                Title = Path.GetFileNameWithoutExtension(filename),
                FileName = filename[..Math.Min(240, filename.Length)],
                StorageName = name,
                MimeType = mime,
                SizeBytes = file.Length,
                Sha256 = hash,
                Status = "parsing"
            };
            db.Documents.Add(doc);
            await db.SaveChangesAsync(CancellationToken.None);
            retained = true;
            var sections = await ProcessDocumentAsync(db, store, doc, path, true, ct);
            return Results.Ok(new { document = PublicDocument(doc), sections });
        }
        finally
        {
            if (!retained && path != null && File.Exists(path)) File.Delete(path);
            ImportGate.Release();
        }
    }

    private static async Task<IResult> Retry(Guid id, AppDbContext db, DocumentStore store, CancellationToken ct)
    {
        if (!await ImportGate.WaitAsync(0, ct)) return Results.Problem("Another document is being imported. Please try again shortly.", statusCode: 429);
        try
        {
            var doc = await db.Documents.FindAsync([id], ct);
            if (doc == null) return Results.NotFound();
            var path = store.PathFor(doc.StorageName);
            if (!File.Exists(path))
            {
                doc.Status = "failed";
                doc.Error = "The original file is missing. Upload the same file again or restore the uploads volume from your backup to retry extraction.";
                await db.SaveChangesAsync(CancellationToken.None);
                return Results.Problem(doc.Error, statusCode: 409);
            }
            var sections = await ProcessDocumentAsync(db, store, doc, path, false, ct);
            return Results.Ok(new { document = PublicDocument(doc), sections });
        }
        finally { ImportGate.Release(); }
    }

    private static async Task<List<DocumentSection>> ProcessDocumentAsync(AppDbContext db, DocumentStore store,
        LibraryDocument doc, string path, bool initialImport, CancellationToken ct)
    {
        // Persist the attempt first. A terminated process leaves a visible, retryable parsing record.
        doc.Status = "parsing";
        doc.Error = null;
        await db.SaveChangesAsync(CancellationToken.None);
        ParsedDocument parsed;
        try { parsed = await store.ParseAsync(path, doc.MimeType == "application/pdf" ? "pdf" : "epub", ct); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            parsed = new(null, null, 0, [], e is OperationCanceledException
                ? "Text extraction was interrupted. Your original file and existing reading data are retained; retry extraction when ready."
                : "Text extraction stopped or exceeded its safety limits. Your original file and existing reading data are retained; you can retry extraction.");
        }
        // Recovery must not overwrite the owner's edited document metadata.
        if (initialImport)
        {
            doc.Title = string.IsNullOrWhiteSpace(parsed.Title) ? doc.Title : parsed.Title[..Math.Min(500, parsed.Title.Length)];
            doc.Author = parsed.Author;
        }
        if (parsed.PageCount > 0) doc.PageCount = parsed.PageCount;
        doc.Error = parsed.Warning;
        doc.Status = parsed.Sections.Count == 0 ? "failed" : parsed.Sections.All(s => string.IsNullOrWhiteSpace(s.Text)) ? "original-only" : "ready";
        var sections = await db.Sections.Where(s => s.DocumentId == doc.Id).OrderBy(s => s.Order).ToListAsync(CancellationToken.None);
        var items = await db.Items.Where(i => i.DocumentId == doc.Id).ToDictionaryAsync(i => i.Id, CancellationToken.None);
        foreach (var item in items.Values) item.ImportWarning = doc.Error;
        // Keep prior extracted text on failure, along with every section identity and user-created range.
        if (parsed.Sections.Count == 0 && sections.Count == 0) parsed.Sections.Add(new(doc.Title, "", "original"));
        var nextOrder = sections.Count == 0 ? 0 : sections.Max(s => s.Order) + 1;
        foreach (var parsedSection in parsed.Sections)
        {
            var existing = sections.FirstOrDefault(s => s.Locator == parsedSection.Locator);
            if (existing == null)
            {
                sections.Add(await AddSectionAsync(db, doc, parsedSection, nextOrder++, CancellationToken.None));
                continue;
            }
            if (!string.IsNullOrWhiteSpace(parsedSection.Text) || string.IsNullOrWhiteSpace(existing.Text))
            {
                existing.Text = parsedSection.Text;
                existing.StartPage = parsedSection.StartPage;
                existing.EndPage = parsedSection.EndPage;
                if (items.TryGetValue(existing.ItemId, out var item))
                {
                    item.Body = parsedSection.Text;
                    item.Excerpt = parsedSection.Text[..Math.Min(600, parsedSection.Text.Length)];
                    item.UpdatedAt = DateTimeOffset.UtcNow;
                    await TopicClassification.AssignAsync(db, item, [], CancellationToken.None);
                }
            }
        }
        // Finish even after a disconnected request, so cancellation cannot strand a retained file.
        await db.SaveChangesAsync(CancellationToken.None);
        return sections;
    }

    private static async Task<DocumentSection> AddSectionAsync(AppDbContext db, LibraryDocument doc, ParsedSection parsed, int order, CancellationToken ct)
    {
        var section = new DocumentSection
        {
            DocumentId = doc.Id,
            Title = parsed.Title,
            Order = order,
            Text = parsed.Text,
            Locator = parsed.Locator,
            StartPage = parsed.StartPage,
            EndPage = parsed.EndPage
        };
        var item = new ContentItem
        {
            Title = parsed.Title,
            Author = doc.Author,
            Kind = "book",
            Provider = "local-upload",
            CanonicalUrl = $"local://document/{doc.Id}/section/{section.Id}",
            DocumentId = doc.Id,
            SectionId = section.Id,
            Body = parsed.Text,
            Excerpt = parsed.Text[..Math.Min(600, parsed.Text.Length)],
            TextProvenance = "personal-upload",
            ImportWarning = doc.Error
        };
        section.ItemId = item.Id;
        item.ClassificationOrigin = "document-keywords";
        db.Items.Add(item); db.Sections.Add(section);
        await TopicClassification.AssignAsync(db, item, [], ct);
        await ItemCatalog.RegisterAsync(db, item, CatalogUpgrade.RecordIdentities(item), ct);
        return section;
    }

    private static object PublicDocument(LibraryDocument d) => new
    {
        d.Id,
        d.Title,
        d.Author,
        d.FileName,
        d.MimeType,
        d.SizeBytes,
        d.Status,
        d.Error,
        d.PageCount,
        d.CreatedAt
    };
    public record BookEdit(string Title, string? Author);
    public record RangeEdit(string Title, int StartPage, int EndPage);
}

public sealed class DocumentStore
{
    private readonly string root;
    public DocumentStore(IConfiguration configuration)
    {
        root = Path.GetFullPath(configuration["Documents:Path"] ?? Path.Combine(AppContext.BaseDirectory, "uploads"));
        Directory.CreateDirectory(root);
    }
    public string PathFor(string name)
    {
        if (Path.GetFileName(name) != name || name.IndexOfAny(['/', '\\', ':']) >= 0) throw new InvalidDataException("Invalid storage name.");
        return Path.Combine(root, name);
    }
    public async Task<ParsedDocument> ParseAsync(string input, string kind, CancellationToken ct)
    {
        var output = PathFor(Guid.NewGuid().ToString("N") + ".json");
        var current = Environment.ProcessPath ?? throw new InvalidOperationException();
        var executable = Path.GetFileNameWithoutExtension(current).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? current : "dotnet";
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        start.ArgumentList.Add(typeof(DocumentParser).Assembly.Location);
        foreach (var arg in new[] { "--parse-document", input, kind, output }) start.ArgumentList.Add(arg);
        start.Environment["DOTNET_GCHeapHardLimit"] = "10000000";
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        using var process = Process.Start(start) ?? throw new IOException("Parser could not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length > 30 * 1024 * 1024)
                throw new InvalidDataException("Parser failed or exceeded output limit.");
            return JsonSerializer.Deserialize<ParsedDocument>(await File.ReadAllTextAsync(output, ct)) ?? throw new InvalidDataException();
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
