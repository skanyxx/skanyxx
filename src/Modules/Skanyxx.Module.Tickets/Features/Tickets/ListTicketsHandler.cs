using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Sources;

namespace Skanyxx.Module.Tickets.Features.Tickets;

internal sealed class ListTicketsHandler(ITicketSource source) : IRequestHandler<ListTicketsQuery, Outcome<IReadOnlyList<Ticket>>>
{
    public async Task<Outcome<IReadOnlyList<Ticket>>> Handle(ListTicketsQuery query, CancellationToken ct)
    {
        try
        {
            var tickets = await source.ListAsync(query.Assignee is null ? query.Limit : TicketScan.Size, ct);
            return Outcome<IReadOnlyList<Ticket>>.Ok(tickets.Where(t => Matches(t, query.Assignee)).Take(query.Limit).ToList());
        }
        catch (TicketSourceException ex)
        {
            return Outcome<IReadOnlyList<Ticket>>.Unavailable(ex.Message);
        }
    }

    private static bool Matches(Ticket ticket, string? assignee) => assignee switch
    {
        null => true,
        ListTicketsQuery.Unassigned => ticket.Assignee is null,
        _ => string.Equals(ticket.Assignee, assignee.Trim(), StringComparison.OrdinalIgnoreCase)
    };
}
