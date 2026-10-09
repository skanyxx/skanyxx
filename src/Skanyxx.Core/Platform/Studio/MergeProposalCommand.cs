using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// A supervisor merges (D024): the files at the PR's head are validated, merged pinned to that head, and the reconciler
/// applies main to kagent and memory at once (D045). A team or department grant needs the owner (D091).
/// </summary>
public sealed record MergeProposalCommand(StudioUser User, int Number) : IRequest<Outcome<StudioResult>>;
