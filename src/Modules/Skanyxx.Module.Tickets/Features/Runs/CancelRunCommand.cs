using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Runs;

public sealed record CancelRunCommand(string? UserId, Guid Id) : IRequest<Outcome<Run>>;
