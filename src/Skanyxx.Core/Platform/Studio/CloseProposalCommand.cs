using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>Rejects a proposal (a supervisor, or the builder who proposed it); its preview is removed.</summary>
public sealed record CloseProposalCommand(StudioUser User, int Number) : IRequest<Outcome<StudioResult>>;
