using System.Net;
using System.Net.Sockets;

namespace RabbitHole.Ingestion;

public sealed record RemoteRequest(Uri Url, string[] AllowedMimeTypes, int MaxBytes = 1_000_000,
    string? ETag = null, string? LastModified = null);
public sealed record RemoteResponse(int StatusCode, Uri FinalUrl, string ContentType, byte[] Body,
    string? ETag, string? LastModified)
{
    public string Text => System.Text.Encoding.UTF8.GetString(Body);
};
public interface IRemoteFetcher
{
    Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken);
}
public interface IDnsLookup
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}
public sealed class SystemDnsLookup : IDnsLookup
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class RemoteFetchException(string message) : Exception(message);

public sealed class SafeRemoteFetcher(IDnsLookup dns) : IRemoteFetcher
{
    public static Uri ValidateRedirect(Uri current, Uri location) =>
        ValidateUrl(new Uri(current, location).ToString());

    public static Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.HostNameType == UriHostNameType.Unknown ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
            !((uri.Scheme == "https" && uri.Port == 443) || (uri.Scheme == "http" && uri.Port == 80)))
            throw new RemoteFetchException("Only public HTTP(S) URLs on standard ports are allowed.");
        if (IPAddress.TryParse(uri.Host, out var address) && !IsPublic(address))
            throw new RemoteFetchException("Private and reserved network addresses are blocked.");
        return uri;
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] is not (0 or 10 or 127) && b[0] < 224 &&
                   !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
                   !(b[0] == 169 && b[1] == 254) &&
                   !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
                   !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 88)) &&
                   !(b[0] == 198 && b[1] is 18 or 19) &&
                   !(b[0] == 192 && b[1] == 0) &&
                   !(b[0] == 192 && b[1] == 0 && b[2] == 2) &&
                   !(b[0] == 198 && b[1] == 51 && b[2] == 100) &&
                   !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
        var bytes = address.GetAddressBytes();
        return !IPAddress.IPv6Loopback.Equals(address) && !IPAddress.IPv6Any.Equals(address) &&
               bytes[0] is >= 0x20 and <= 0x3f &&
               !(bytes[0] == 0x20 && bytes[1] == 0x02) &&
               !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0) &&
               !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }

    public async Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken)
    {
        var current = ValidateUrl(request.Url.ToString());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        for (var redirects = 0; redirects <= 4; redirects++)
        {
            var addresses = IPAddress.TryParse(current.Host, out var literal)
                ? [literal] : await dns.ResolveAsync(current.DnsSafeHost, deadline.Token);
            if (addresses.Length == 0 || addresses.Any(ip => !IsPublic(ip)))
                throw new RemoteFetchException("The host resolves to a private or reserved network address.");
            var pinned = addresses[0];
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(pinned.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(pinned, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                }
            };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var message = new HttpRequestMessage(HttpMethod.Get, current);
            message.Headers.UserAgent.ParseAdd("RabbitHole/1.0 (private feed reader)");
            message.Headers.Accept.ParseAdd("*/*");
            if (request.ETag is { Length: > 0 }) message.Headers.TryAddWithoutValidation("If-None-Match", request.ETag);
            if (request.LastModified is { Length: > 0 }) message.Headers.TryAddWithoutValidation("If-Modified-Since", request.LastModified);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.NotModified)
                return new RemoteResponse(304, current, "", [], response.Headers.ETag?.ToString(), response.Content.Headers.LastModified?.ToString("R"));
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                if (response.Headers.Location is null) throw new RemoteFetchException("Redirect has no destination.");
                current = ValidateRedirect(current, response.Headers.Location);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new RemoteFetchException($"Remote source returned HTTP {(int)response.StatusCode}.");
            var mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            if (!request.AllowedMimeTypes.Contains(mime, StringComparer.OrdinalIgnoreCase))
                throw new RemoteFetchException($"Unsupported response type: {mime}.");
            if (response.Content.Headers.ContentLength > request.MaxBytes)
                throw new RemoteFetchException("Remote response exceeds the size limit.");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (output.Length + count > request.MaxBytes) throw new RemoteFetchException("Remote response exceeds the size limit.");
                output.Write(buffer, 0, count);
            }
            return new RemoteResponse((int)response.StatusCode, current, mime, output.ToArray(),
                response.Headers.ETag?.ToString(), response.Content.Headers.LastModified?.ToString("R"));
        }
        throw new RemoteFetchException("Too many redirects.");
    }
}
