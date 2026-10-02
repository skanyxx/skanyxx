using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Tickets;

/// <summary><see cref="Assignee"/> is a display name, or <c>unassigned</c>; null means no filter.</summary>
public sealed record ListTicketsQuery(string? UserId, string? Assignee, int Limit) : IRequest<Outcome<IReadOnlyList<Ticket>>>
{
    public const string Unassigned = "unassigned";
}
