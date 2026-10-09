namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>The token travels in the body, never the URL.</summary>
public sealed class ResetPasswordRequest
{
    public string? Token { get; set; }
    public string? Password { get; set; }
}
