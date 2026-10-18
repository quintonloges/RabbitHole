using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using RabbitHole.Features;
using Xunit;

namespace RabbitHole.Tests;

public class ExportSanitizerTests
{
    private const string Secret = "TEST_SECRET_DO_NOT_EXPORT";

    [Theory]
    [InlineData("secret_token")]
    [InlineData("API-Key")]
    [InlineData("access%5Ftoken")]
    [InlineData("X-Amz-Signature")]
    [InlineData("password")]
    [InlineData("client_id")]
    [InlineData("secret_key")]
    [InlineData("AWSAccessKeyId")]
    public void UrlCredentialsAreRedactedWhileOrdinaryQueryValuesRemain(string key)
    {
        var original = $"https://example.org/article?topic=mind&{key}={Secret}&page=2";
        var result = ExportSanitizer.RedactUrl(original);
        Assert.DoesNotContain(Secret, result);
        var query = QueryHelpers.ParseQuery(new Uri(result).Query);
        Assert.Equal("mind", query["topic"].ToString());
        Assert.Equal("2", query["page"].ToString());
        Assert.Contains(Secret, original);
    }

    [Fact]
    public void EncodedPlayerUrlAndProviderIdentifierDoNotExportPrivateTrackToken()
    {
        var url = "https://soundcloud.com/test/track?secret_token=" + Secret + "&color=blue";
        var embed = "https://w.soundcloud.com/player/?url=" + Uri.EscapeDataString(url) + "&auto_play=false";
        var result = ExportSanitizer.RedactUrl(embed);
        Assert.DoesNotContain(Secret, result);
        Assert.DoesNotContain(Secret, ExportSanitizer.RedactIdentifier(url)!);
        var query = QueryHelpers.ParseQuery(new Uri(result).Query);
        Assert.Equal("false", query["auto_play"].ToString());
        Assert.Equal("blue", QueryHelpers.ParseQuery(new Uri(query["url"].ToString()).Query)["color"].ToString());
    }

    [Fact]
    public void NestedJsonReferencesAndSecretPropertiesAreSanitizedWithoutChangingSource()
    {
        var source = JsonSerializer.Serialize(new
        {
            providerReferences = new[] { new { provider = "soundcloud", id = "https://soundcloud.com/test/track?secret_token=" + Secret } },
            detail = new { api_key = Secret, title = "TEST FIXTURE", count = 3, available = true },
            doi = "10.1234/test"
        });
        var result = ExportSanitizer.RedactMetadata(source)!;
        Assert.DoesNotContain(Secret, result);
        using var json = JsonDocument.Parse(result);
        Assert.Equal("soundcloud", json.RootElement.GetProperty("providerReferences")[0].GetProperty("provider").GetString());
        Assert.Equal("TEST FIXTURE", json.RootElement.GetProperty("detail").GetProperty("title").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("detail").GetProperty("count").GetInt32());
        Assert.True(json.RootElement.GetProperty("detail").GetProperty("available").GetBoolean());
        Assert.Equal("10.1234/test", json.RootElement.GetProperty("doi").GetString());
        Assert.Contains(Secret, source);
    }

    [Fact]
    public void MultipleEncodingLayersAndFragmentCredentialsAreRedacted()
    {
        var inner = "https://example.org/private?access_token=" + Secret;
        var encoded = Uri.EscapeDataString(Uri.EscapeDataString(inner));
        var result = ExportSanitizer.RedactUrl("https://example.org/redirect?next=" + encoded + "#refresh_token=" + Secret);
        Assert.DoesNotContain(Secret, result);
        Assert.DoesNotContain(Secret, ExportSanitizer.RedactIdentifier(encoded)!);
    }

    [Fact]
    public void UserInfoIsRemovedAndInnocentUrlsAndOpaqueIdsArePreserved()
    {
        Assert.DoesNotContain(Secret, ExportSanitizer.RedactUrl("https://user:" + Secret + "@example.org/article"));
        const string url = "https://example.org/article?keyword=monkey&topic=mind%20and%20matter&page=2#section";
        Assert.Equal(url, ExportSanitizer.RedactUrl(url));
        Assert.Equal("opaque%2Fprovider-id", ExportSanitizer.RedactIdentifier("opaque%2Fprovider-id"));
        Assert.Null(ExportSanitizer.RedactIdentifier(null));
        Assert.Null(ExportSanitizer.RedactMetadata(null));
    }

    [Fact]
    public void MalformedOrExcessivelyNestedMetadataIsNotCopiedOutUninspected()
    {
        Assert.Null(ExportSanitizer.RedactMetadata("{\"token\":\"" + Secret));
        var metadata = JsonSerializer.Serialize(new { id = "https://example.org/?token=" + Secret });
        for (var i = 0; i < 20; i++) metadata = "{\"nested\":" + metadata + "}";
        var result = ExportSanitizer.RedactMetadata(metadata)!;
        Assert.DoesNotContain(Secret, result);
        using var document = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }
}
