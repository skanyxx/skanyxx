using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Runs;

public sealed record StartRunCommand(string? UserId, string TicketKey, string PipelineId) : IRequest<Outcome<Run>>;
