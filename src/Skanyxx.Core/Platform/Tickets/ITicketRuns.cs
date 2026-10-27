namespace Skanyxx.Core.Platform.Tickets;

/// <summary>
/// Pipeline runs of given tickets, newest first, for the chat index's ticket view (D240). Implemented by the tickets
/// module; optional to its readers (no tickets module, no runs).
/// </summary>
public interface ITicketRuns
{
    Task<IReadOnlyList<TicketRunSummary>> ForTicketsAsync(IReadOnlyCollection<string> ticketKeys, int limit, CancellationToken ct);
}
