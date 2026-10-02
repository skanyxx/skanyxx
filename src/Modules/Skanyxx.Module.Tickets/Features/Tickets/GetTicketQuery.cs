using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Tickets;

public sealed record GetTicketQuery(string? UserId, string Key) : IRequest<Outcome<Ticket>>;
