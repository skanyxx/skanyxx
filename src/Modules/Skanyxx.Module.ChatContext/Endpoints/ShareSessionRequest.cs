namespace Skanyxx.Module.ChatContext.Endpoints;

internal sealed class ShareSessionRequest : SessionRouteRequest
{
    public string? Scope { get; set; }
}
