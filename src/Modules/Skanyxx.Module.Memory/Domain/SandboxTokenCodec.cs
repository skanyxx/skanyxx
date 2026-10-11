using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// D076: <c>skx_sbx_&lt;base64url(JSON claims)&gt;.&lt;base64url(HMAC-SHA256(key, everything before the dot))&gt;</c>.
/// The claims bind the sandbox, its memory agent id, its owner and an expiry, plus a random id so two tokens are never
/// equal. The signature is checked in constant time before any claim is read. Signing proves Skanyxx issued it; the
/// stored hash (<see cref="SandboxToken"/>) proves it is still the live one.
/// </summary>
public static class SandboxTokenCodec
{
    public sealed record Claims(string SandboxName, string AgentId, string OwnerId, DateTimeOffset ExpiresAt);

    private const int Version = 1;
    private const int MacBytes = 32;
    private const int MaxLength = 1024;

    public static string Sign(Claims claims, string key)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(Version, claims.SandboxName, claims.AgentId, claims.OwnerId,
            claims.ExpiresAt.ToUnixTimeSeconds(), WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16))));
        var body = SandboxMemory.TokenPrefix + WebEncoders.Base64UrlEncode(payload);
        return body + "." + WebEncoders.Base64UrlEncode(Mac(body, key));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>The claims of a token this key signed and that has not expired; otherwise null and why (never the value).</summary>
    public static Claims? Read(string token, string key, DateTimeOffset now, out string failure)
    {
        failure = "Not a sandbox token.";
        if (token.Length > MaxLength || !token.StartsWith(SandboxMemory.TokenPrefix, StringComparison.Ordinal))
            return null;
        var dot = token.LastIndexOf('.');
        if (dot <= SandboxMemory.TokenPrefix.Length)
            return null;

        var body = token[..dot];
        byte[] mac, payload;
        try
        {
            mac = WebEncoders.Base64UrlDecode(token[(dot + 1)..]);
            payload = WebEncoders.Base64UrlDecode(body[SandboxMemory.TokenPrefix.Length..]);
        }
        catch (FormatException)
        {
            return null;
        }
        if (mac.Length != MacBytes || !CryptographicOperations.FixedTimeEquals(mac, Mac(body, key)))
        {
            failure = "Sandbox token signature does not match.";
            return null;
        }

        Payload? claims;
        try
        {
            claims = JsonSerializer.Deserialize<Payload>(payload);
        }
        catch (JsonException)
        {
            claims = null;
        }
        if (claims is not { V: Version } || !Identifier.IsValid(claims.Agt) || !Identifier.IsValid(claims.Own) || string.IsNullOrEmpty(claims.Sbx))
        {
            failure = "Sandbox token claims are invalid.";
            return null;
        }
        var expires = DateTimeOffset.FromUnixTimeSeconds(claims.Exp);
        if (expires <= now)
        {
            failure = "Sandbox token expired.";
            return null;
        }
        return new Claims(claims.Sbx, claims.Agt, claims.Own, expires);
    }

    private static byte[] Mac(string body, string key) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body));

    private sealed record Payload(
        [property: JsonPropertyName("v")] int V,
        [property: JsonPropertyName("sbx")] string Sbx,
        [property: JsonPropertyName("agt")] string Agt,
        [property: JsonPropertyName("own")] string Own,
        [property: JsonPropertyName("exp")] long Exp,
        [property: JsonPropertyName("jti")] string Jti);
}
