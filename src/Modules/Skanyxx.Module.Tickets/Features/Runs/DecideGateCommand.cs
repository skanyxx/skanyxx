using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Runs;

public sealed record DecideGateCommand(string? UserId, Guid Id, Decision? Decision, string? Note) : IRequest<Outcome<Run>>;
