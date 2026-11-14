using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("RabbitHole.Api.Tests")]

namespace RabbitHole.Ingestion;

public interface IYtDlpClient
{
    Task<YtDlpPage> FetchAsync(string channelId, int start, int limit, CancellationToken ct);
}

public sealed record YtDlpPage(int SchemaVersion, string Version, string ChannelId, int Start, int NextStart,
    bool Exhausted, IReadOnlyList<YtDlpEntry> Entries, IReadOnlyList<string> Warnings);
public sealed record YtDlpEntry(string Id, string? Title, string? ChannelId, string? Author, string? Description,
    string? Thumbnail, string? UploadDate, string? Availability, bool IsLive, bool IsUpcoming, int Index);
public sealed class YtDlpException(string message, string code = "extractor") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Bounded, isolated helper invocation; no provider credentials or raw subprocess errors escape.</summary>
public sealed class YtDlpClient : IYtDlpClient
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly IConfiguration config;
    private readonly string helperPath;
    private readonly TimeSpan timeout;
    public YtDlpClient(IConfiguration config) : this(config,
        Path.Combine(AppContext.BaseDirectory, "Ingestion", "yt_dlp_archive.py"), TimeSpan.FromSeconds(75))
    { }
    internal YtDlpClient(IConfiguration config, string helperPath, TimeSpan? timeout = null)
    {
        this.config = config; this.helperPath = Path.GetFullPath(helperPath);
        this.timeout = timeout ?? TimeSpan.FromSeconds(75);
    }

    public async Task<YtDlpPage> FetchAsync(string channelId, int start, int limit, CancellationToken ct)
    {
        if (channelId is null || !Regex.IsMatch(channelId, @"^UC[A-Za-z0-9_-]{22}$") || start is < 1 or > 200_000 || limit is < 1 or > 50)
            throw new ArgumentException("Invalid YouTube archive channel or page range.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var acquired = false;
        Process? process = null;
        try
        {
            await Gate.WaitAsync(deadline.Token); acquired = true;
            if (!File.Exists(helperPath)) throw Error("helper-missing");
            var executable = config["YouTube:YtDlpPython"] ?? config["YTDLP_PYTHON"];
            if (string.IsNullOrWhiteSpace(executable)) executable = OperatingSystem.IsWindows() ? "python" : "python3";
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            // Preserve OS/runtime lookup only. Never forward application keys, proxies or Python overrides.
            var inherited = info.Environment.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            info.Environment.Clear();
            foreach (var name in new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA" })
                if (inherited.TryGetValue(name, out var value)) info.Environment[name] = value;
            foreach (var argument in new[] { "-X", "utf8", "-I", helperPath, "--channel", channelId, "--start", start.ToString(CultureInfo.InvariantCulture), "--limit", limit.ToString(CultureInfo.InvariantCulture) })
                info.ArgumentList.Add(argument);
            process = new Process { StartInfo = info };
            try { if (!process.Start()) throw Error("executable-missing"); }
            catch (Exception error) when (error is Win32Exception or ArgumentException) { throw Error("executable-missing"); }
            process.StandardInput.Close();
            var stdout = ReadAsync(process.StandardOutput.BaseStream, 500_000, process, deadline.Token);
            var stderr = ReadAsync(process.StandardError.BaseStream, 16_384, process, deadline.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token));
            return Parse(await stdout, process.ExitCode, channelId, start, limit);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Error("timeout"); }
        catch (OperationCanceledException) { throw; }
        catch (YtDlpException) { throw; }
        catch (Exception error) when (error is IOException or InvalidOperationException or JsonException or DecoderFallbackException)
        { throw Error("invalid-output"); }
        finally
        {
            if (process is not null)
            {
                Kill(process);
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (Exception error) when (error is InvalidOperationException or TimeoutException or Win32Exception) { }
                process.Dispose();
            }
            if (acquired) Gate.Release();
        }
    }

    private static async Task<string> ReadAsync(Stream stream, int maxBytes, Process process, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(bytes, ct);
            if (count == 0) break;
            if (buffer.Length + count > maxBytes) { Kill(process); throw Error("output-limit"); }
            buffer.Write(bytes, 0, count);
        }
        return new UTF8Encoding(false, true).GetString(buffer.ToArray());
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or NotSupportedException) { }
    }

    private static YtDlpPage Parse(string json, int exitCode, string channel, int start, int limit)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw Error("protocol");
        JsonElement Field(string name) => root.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        if (Field("schemaVersion").ValueKind != JsonValueKind.Number || !Field("schemaVersion").TryGetInt32(out var schema) || schema != 1 ||
            Field("version").ValueKind != JsonValueKind.String || Field("version").GetString()!.Length > 64)
            throw Error("protocol");
        if (Field("errorCode").ValueKind == JsonValueKind.String) throw Error(Field("errorCode").GetString()!);
        if (!Regex.IsMatch(Field("version").GetString() ?? "", @"^[0-9][A-Za-z0-9.+_-]{1,63}$")) throw Error("protocol");
        if (exitCode != 0) throw Error("extractor");
        foreach (var field in new[] { "channelId", "start", "nextStart", "exhausted", "entries", "warnings" })
            if (Field(field).ValueKind == JsonValueKind.Undefined) throw Error("protocol");
        var page = JsonSerializer.Deserialize<YtDlpPage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 16 });
        if (page is null || page.ChannelId != channel || page.Start != start || page.NextStart < start || page.NextStart > start + limit ||
            !page.Exhausted && page.NextStart == start || page.Entries is null || page.Entries.Count > limit || page.Warnings is null || page.Warnings.Count > 20)
            throw Error("protocol");
        foreach (var element in Field("entries").EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw Error("protocol");
            JsonElement EntryField(string name) => element.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
            if (EntryField("id").ValueKind != JsonValueKind.String || EntryField("index").ValueKind != JsonValueKind.Number ||
                EntryField("isLive").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                EntryField("isUpcoming").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Error("protocol");
        }
        var last = start - 1;
        foreach (var entry in page.Entries)
        {
            if (entry is null || !Regex.IsMatch(entry.Id ?? "", @"^[A-Za-z0-9_-]{11}$") ||
                entry.ChannelId is not null && entry.ChannelId != channel || entry.Index <= last || entry.Index < start || entry.Index >= page.NextStart ||
                !Bounded(entry.Title, 1000) || !Bounded(entry.Author, 400) || !Bounded(entry.Description, 5000) || !Bounded(entry.Thumbnail, 2048) ||
                entry.Availability is not null && !Regex.IsMatch(entry.Availability, @"^[a-z_-]{1,40}$") ||
                entry.UploadDate is not null && !DateOnly.TryParseExact(entry.UploadDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw Error("protocol");
            last = entry.Index;
        }
        if (page.Warnings.Any(w => w is null || w.Length > 200 || w.Any(char.IsControl))) throw Error("protocol");
        return page;
    }
    private static bool Bounded(string? value, int max) => value is null || value.Length <= max;
    private static YtDlpException Error(string code) => code switch
    {
        "helper-missing" => new("yt-dlp archive helper is unavailable on this server.", code),
        "executable-missing" => new("yt-dlp archive requires an available Python executable on this server.", code),
        "dependency-missing" => new("yt-dlp is not installed for the configured Python executable.", code),
        "version-mismatch" => new("yt-dlp version does not match this release. Rebuild the API image or reinstall the pinned helper dependency.", code),
        "blocked" => new("YouTube blocked this yt-dlp request. Retry later; no credentials or cookies were used.", code),
        "rate-limited" => new("YouTube rate-limited yt-dlp. Retry later.", code),
        "network" => new("yt-dlp could not reach YouTube. Check server connectivity and retry.", code),
        "timeout" => new("yt-dlp archive request timed out. Retry is available.", code),
        "request-limit" => new("yt-dlp reached this release’s archive scan limit. Automatic archive checks are paused; Check YouTube or Find older now can retry.", code),
        "output-limit" => new("yt-dlp returned more output than permitted. No archive progress was saved.", code),
        "extractor" => new("yt-dlp could not read the channel archive. Check its installed version and retry.", code),
        _ => new("yt-dlp returned invalid archive metadata. No archive progress was saved.", "invalid-output")
    };
}
