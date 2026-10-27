namespace Skanyxx.Module.ChatContext.Endpoints;

internal sealed class LinkTicketsRequest : SessionRouteRequest
{
    public string[]? TicketKeys { get; set; }
}
