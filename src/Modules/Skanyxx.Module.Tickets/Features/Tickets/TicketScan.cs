namespace Skanyxx.Module.Tickets.Features.Tickets;

/// <summary>
/// Filters run in memory over the first page (Jira's page cap), not in JQL: what we hold is a display name,
/// which Jira may not resolve, and a query-string value must stay out of a query language.
/// </summary>
internal static class TicketScan
{
    public const int Size = 100;
}
