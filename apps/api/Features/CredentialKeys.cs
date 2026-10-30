using System.Text.RegularExpressions;

namespace RabbitHole.Features;

/// <summary>Shared classification for rejecting public URLs and redacting private exports.</summary>
public static class CredentialKeys
{
    public static bool IsSecret(string key)
    {
        for (var i = 0; i < 8; i++)
        {
            var decoded = Uri.UnescapeDataString(key);
            if (decoded == key) break;
            key = decoded;
            if (i == 7 && Uri.UnescapeDataString(key) != key) return true;
        }
        var normalized = Regex.Replace(key.ToLowerInvariant(), "[^a-z0-9]", "");
        return normalized is "key" or "auth" or "authorization" or "sig" or "pwd" or "passwd" or "clientid" or "credentials"
            or "secretkey" or "privatekey" or "subscriptionkey" or "accountkey" or "signingkey" or "accesskey" or "awsaccesskeyid"
            or "code" or "jwt" or "session" or "sessionid" or "ticket" ||
            normalized.EndsWith("apikey", StringComparison.Ordinal) ||
            normalized.EndsWith("token", StringComparison.Ordinal) ||
            normalized.EndsWith("secret", StringComparison.Ordinal) ||
            normalized.EndsWith("password", StringComparison.Ordinal) ||
            normalized.EndsWith("signature", StringComparison.Ordinal) ||
            normalized.EndsWith("credential", StringComparison.Ordinal) ||
            normalized.StartsWith("xamz", StringComparison.Ordinal) ||
            normalized.StartsWith("xgoog", StringComparison.Ordinal);
    }
}
