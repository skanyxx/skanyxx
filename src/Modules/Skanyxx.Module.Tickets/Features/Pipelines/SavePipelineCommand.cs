using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

/// <summary>Create or replace. Runs already started keep the snapshot they were started with.</summary>
public sealed record SavePipelineCommand(string? UserId, string Id, string Name, string Description, IReadOnlyList<PipelineStage> Stages)
    : IRequest<Outcome<Pipeline>>;
