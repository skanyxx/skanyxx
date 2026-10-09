using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>The repo, its open proposals and the agents in main. Builders and supervisors (D022).</summary>
public sealed record StudioOverviewQuery(StudioUser User) : IRequest<Outcome<StudioOverview>>;
