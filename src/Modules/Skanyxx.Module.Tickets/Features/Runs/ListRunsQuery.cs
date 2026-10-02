using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;

namespace Skanyxx.Module.Tickets.Features.Runs;

/// <summary>Newest first, at most <see cref="Limit"/>; optionally one ticket's runs.</summary>
public sealed record ListRunsQuery(string? UserId, string? TicketKey) : IRequest<Outcome<IReadOnlyList<RunSummaryDto>>>
{
    public const int Limit = 100;
}
