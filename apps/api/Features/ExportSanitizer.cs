using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RabbitHole.Features;

/// <summary>Produces export copies; persisted provider values remain untouched.</summary>
public static class ExportSanitizer
{
    private const string Redacted = "[redacted]";
    private const int MaxDepth = 8;
    private const int MaxMetadataLength = 1_000_000;
    private const int MaxUrlLength = 65_536;

    public static string RedactUrl(string value) => RedactValue(value, 0);
    public static string? RedactIdentifier(string? value) => value is null ? null : RedactValue(value, 0);

    // Malformed or excessive metadata is omitted: opaque text cannot be safely exported as JSON.
    public static string? RedactMetadata(string? json) => json is null ? null : SanitizeJson(json, 0);

    private static string? SanitizeJson(string json, int depth)
    {
        if (json.Length > MaxMetadataLength || depth >= MaxDepth) return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) WriteJson(writer, document.RootElement, depth);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException) { return null; }
    }

    private static void WriteJson(Utf8JsonWriter writer, JsonElement element, int depth)
    {
        if (depth >= MaxDepth) { writer.WriteStringValue(Redacted); return; }
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSecretKey(property.Name)) writer.WriteStringValue(Redacted);
                    else WriteJson(writer, property.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in element.EnumerateArray()) WriteJson(writer, child, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(RedactValue(element.GetString()!, depth + 1));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string RedactValue(string value, int depth)
    {
        if (value.Length > MaxUrlLength || depth >= MaxDepth) return Redacted;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var query = RedactParameters(uri.Query.TrimStart('?'), depth);
            var fragment = RedactParameters(uri.Fragment.TrimStart('#'), depth);
            if (query == uri.Query.TrimStart('?') && fragment == uri.Fragment.TrimStart('#') && uri.UserInfo.Length == 0)
                return value;
            var builder = new UriBuilder(uri) { UserName = "", Password = "", Query = query, Fragment = fragment };
            return builder.Uri.AbsoluteUri;
        }
        var decoded = Decode(value);
        if (decoded != value)
        {
            var sanitized = RedactValue(decoded, depth + 1);
            return sanitized == decoded ? value : Uri.EscapeDataString(sanitized);
        }
        if (value.TrimStart().StartsWith('{') || value.TrimStart().StartsWith('['))
            return SanitizeJson(value, depth + 1) ?? Redacted;
        return value;
    }

    private static string RedactParameters(string parameters, int depth)
    {
        // Retain ordering, duplicate keys, and innocent query values byte for byte.
        var parts = Regex.Split(parameters, "([&;])");
        for (var i = 0; i < parts.Length; i += 2)
        {
            var separator = parts[i].IndexOf('=');
            if (separator < 0) continue;
            var key = parts[i][..separator];
            var value = parts[i][(separator + 1)..];
            parts[i] = key + "=" + (IsSecretKey(key) ? Uri.EscapeDataString(Redacted) : RedactValue(value, depth + 1));
        }
        return string.Concat(parts);
    }

    private static bool IsSecretKey(string key)
    {
        for (var i = 0; i < MaxDepth; i++)
        {
            var decoded = Decode(key);
            if (decoded == key) break;
            key = decoded;
            if (i == MaxDepth - 1 && Decode(key) != key) return true;
        }
        var normalized = Regex.Replace(key.ToLowerInvariant(), "[^a-z0-9]", "");
        return normalized is "key" or "auth" or "authorization" or "sig" or "pwd" or "passwd" or "clientid" or "credentials"
            or "secretkey" or "privatekey" or "subscriptionkey" or "accountkey" or "signingkey" or "accesskey" or "awsaccesskeyid" ||
            normalized.EndsWith("apikey", StringComparison.Ordinal) ||
            normalized.EndsWith("token", StringComparison.Ordinal) ||
            normalized.EndsWith("secret", StringComparison.Ordinal) ||
            normalized.EndsWith("password", StringComparison.Ordinal) ||
            normalized.EndsWith("signature", StringComparison.Ordinal) ||
            normalized.EndsWith("credential", StringComparison.Ordinal);
    }

    private static string Decode(string value)
    {
        try { return Uri.UnescapeDataString(value); }
        catch (UriFormatException) { return value; }
    }
}
