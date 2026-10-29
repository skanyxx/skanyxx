using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Runs;

/// <summary>Newest first. <paramref name="MineOnly"/> narrows a supervisor's list to their own runs; a builder only ever has theirs.</summary>
public sealed record ListRunsQuery(Viewer Viewer, Guid? WorkflowId, RunState? State, bool MineOnly, string? Search, int Limit)
    : IRequest<Outcome<IReadOnlyList<RunSummaryDto>>>;
