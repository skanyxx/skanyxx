using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>An open proposal's files as they will be merged, and whether they are valid.</summary>
public sealed record ProposalQuery(StudioUser User, int Number) : IRequest<Outcome<ProposalDetail>>;
