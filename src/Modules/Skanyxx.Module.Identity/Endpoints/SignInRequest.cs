namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Nullable because JSON can send null; the validator turns a missing value into a 400.</summary>
public sealed class SignInRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }

    /// <summary>True for a browser session cookie; false (default) returns bearer + refresh tokens.</summary>
    public bool UseCookie { get; set; }
}
