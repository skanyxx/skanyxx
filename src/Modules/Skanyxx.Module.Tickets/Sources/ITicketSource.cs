using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Sources;

/// <summary>Where tickets come from. Read-only by construction: the port has no write verb.</summary>
public interface ITicketSource
{
    Task<IReadOnlyList<Ticket>> ListAsync(int limit, CancellationToken ct);
    Task<Ticket> GetAsync(string key, CancellationToken ct);
}
