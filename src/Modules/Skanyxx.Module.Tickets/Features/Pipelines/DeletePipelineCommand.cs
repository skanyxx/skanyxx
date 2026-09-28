using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

/// <summary>Removes a pipeline. Runs keep their snapshot; the default pipeline is re-seeded on the next start.</summary>
public sealed record DeletePipelineCommand(string? UserId, bool IsSupervisor, string Id) : IRequest<Outcome<Pipeline>>;
