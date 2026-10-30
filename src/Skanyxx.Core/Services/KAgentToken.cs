using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Skanyxx.Core.Models;

namespace Skanyxx.Core.Services;

/// <summary>
/// D521: the token Skanyxx signs for every call to the kagent gate. A JWT, HS256 over <c>KAgent:Auth:TokenKey</c>:
/// issuer <see cref="Issuer"/>, audience <see cref="Audience"/>, <c>sub</c> the person the call is for (or Skanyxx's own
/// user), expiring after <c>KAgent:Auth:TokenSeconds</c>. The gate checks all of them and names the user from <c>sub</c>;
/// kagent itself checks nothing (D101).
/// </summary>
public sealed class KAgentToken(KAgentAuthConfig config, TimeProvider time)
{
    public const string Issuer = "skanyxx";
    public const string Audience = "kagent-gate";

    private static readonly string Header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
    private readonly byte[] _key = Encoding.UTF8.GetBytes(config.TokenKey!);

    public string Issue(string subject)
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        var claims = new { iss = Issuer, aud = Audience, sub = subject, iat = now, exp = now + config.TokenSeconds, jti = Guid.NewGuid().ToString("N") };
        var signed = $"{Header}.{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(claims))}";
        return $"{signed}.{Base64Url.EncodeToString(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(signed)))}";
    }
}
