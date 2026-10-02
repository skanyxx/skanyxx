using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Sources;

namespace Skanyxx.Module.Tickets.Features.Tickets;

/// <summary>Who is on the board, with a count each — the filter's options without fetching every ticket to build them.</summary>
internal sealed class ListAssigneesHandler(ITicketSource source) : IRequestHandler<ListAssigneesQuery, Outcome<IReadOnlyList<AssigneeCount>>>
{
    public async Task<Outcome<IReadOnlyList<AssigneeCount>>> Handle(ListAssigneesQuery query, CancellationToken ct)
    {
        try
        {
            var tickets = await source.ListAsync(TicketScan.Size, ct);
            return Outcome<IReadOnlyList<AssigneeCount>>.Ok(tickets
                .GroupBy(t => t.Assignee ?? ListTicketsQuery.Unassigned)
                .Select(g => new AssigneeCount(g.Key, g.Count()))
                .OrderByDescending(a => a.Count).ThenBy(a => a.Name)
                .ToList());
        }
        catch (TicketSourceException ex)
        {
            return Outcome<IReadOnlyList<AssigneeCount>>.Unavailable(ex.Message);
        }
    }
}
