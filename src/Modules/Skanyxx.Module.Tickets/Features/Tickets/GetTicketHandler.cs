using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Sources;

namespace Skanyxx.Module.Tickets.Features.Tickets;

internal sealed class GetTicketHandler(ITicketSource source) : IRequestHandler<GetTicketQuery, Outcome<Ticket>>
{
    public async Task<Outcome<Ticket>> Handle(GetTicketQuery query, CancellationToken ct)
    {
        try
        {
            return Outcome<Ticket>.Ok(await source.GetAsync(query.Key, ct));
        }
        catch (TicketNotFoundException ex)
        {
            return Outcome<Ticket>.NotFound(ex.Message);
        }
        catch (TicketSourceException ex)
        {
            return Outcome<Ticket>.Unavailable(ex.Message);
        }
    }
}
