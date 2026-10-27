using Skanyxx.Core.Platform.Tickets;

namespace Skanyxx.Module.ChatContext.Tests.Infrastructure;

/// <summary>The tickets module's runs, as a list a test fills.</summary>
public sealed class FakeTicketRuns : ITicketRuns
{
    public List<TicketRunSummary> Runs { get; } = [];

    public Task<IReadOnlyList<TicketRunSummary>> ForTicketsAsync(IReadOnlyCollection<string> ticketKeys, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TicketRunSummary>>([.. Runs.Where(r => ticketKeys.Contains(r.TicketKey)).Take(limit)]);
}
