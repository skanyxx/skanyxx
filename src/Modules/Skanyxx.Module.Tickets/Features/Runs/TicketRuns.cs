using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform.Tickets;
using Skanyxx.Module.Tickets.Data;

namespace Skanyxx.Module.Tickets.Features.Runs;

/// <summary>Runs of some tickets for the chat index's ticket view (D240); readable by anyone signed in, as <c>GET api/tickets/runs</c> is.</summary>
internal sealed class TicketRuns(TicketsDbContext db) : ITicketRuns
{
    public async Task<IReadOnlyList<TicketRunSummary>> ForTicketsAsync(IReadOnlyCollection<string> ticketKeys, int limit, CancellationToken ct)
    {
        var runs = await db.Runs.AsNoTracking()
            .Where(r => ticketKeys.Contains(r.TicketKey))
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .Select(r => new { r.Id, r.TicketKey, r.PipelineName, r.State, r.CreatedBy, r.CreatedAt, r.UpdatedAt })
            .ToListAsync(ct);
        return [.. runs.Select(r => new TicketRunSummary(r.Id, r.TicketKey, r.PipelineName, JsonNamingPolicy.SnakeCaseLower.ConvertName(r.State.ToString()), r.CreatedBy, r.CreatedAt, r.UpdatedAt))];
    }
}
