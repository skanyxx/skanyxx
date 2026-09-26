using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;

namespace Skanyxx.Module.Tickets.Features.Tickets;

public sealed record ListAssigneesQuery(string? UserId) : IRequest<Outcome<IReadOnlyList<AssigneeCount>>>;
