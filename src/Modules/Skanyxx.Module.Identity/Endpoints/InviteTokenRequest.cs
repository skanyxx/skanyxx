namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>The token travels in the body, never the URL, so request logs and traces do not record it.</summary>
public sealed class InviteTokenRequest
{
    public string? Token { get; set; }
}
