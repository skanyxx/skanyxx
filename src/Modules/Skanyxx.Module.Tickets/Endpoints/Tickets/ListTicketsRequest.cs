namespace Skanyxx.Module.Tickets.Endpoints.Tickets;

public sealed class ListTicketsRequest
{
    public string? Assignee { get; set; }
    public int Limit { get; set; } = 50;
}
