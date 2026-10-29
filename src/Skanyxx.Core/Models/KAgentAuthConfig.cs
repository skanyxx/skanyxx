namespace Skanyxx.Core.Models;

/// <summary>
/// <c>KAgent:Auth</c> (D521): how Skanyxx proves itself to kagent. <see cref="None"/> (the default, a local kagent on
/// localhost:8083) sends no token and names the user with <c>?user_id=</c> / <c>X-User-Id</c>, which kagent believes.
/// <see cref="Signed"/> (the Helm chart, behind the kagent gate) signs every request with <see cref="TokenKey"/>; the
/// gate refuses anything else. <c>KAgent:Token</c> is not sent then.
/// </summary>
public sealed class KAgentAuthConfig
{
    public const string None = "none";
    public const string Signed = "signed";

    public string Mode { get; set; } = None;

    /// <summary>The HMAC key, shared with the gate (its JWKS holds the same bytes). 32+ characters.</summary>
    public string? TokenKey { get; set; }

    /// <summary>Each token's life; one is signed per request, so it only has to outlive the request's start.</summary>
    public int TokenSeconds { get; set; } = 60;

    /// <summary>Whether requests are signed; throws on a setting that would leave the gate refusing every call.</summary>
    public bool IsSigned()
    {
        if (Mode is not (None or Signed))
            throw new InvalidOperationException($"KAgent:Auth:Mode must be {None} or {Signed}, not '{Mode}'.");
        if (Mode == None)
            return false;
        if (TokenKey is not { Length: >= 32 })
            throw new InvalidOperationException("KAgent:Auth:Mode=signed needs KAgent:Auth:TokenKey (32+ characters, the kagent gate's key).");
        if (TokenSeconds is < 10 or > 300)
            throw new InvalidOperationException("KAgent:Auth:TokenSeconds must be 10-300: the kagent gate refuses a token that lives longer than 300 s.");
        return true;
    }
}
