using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// The owner's confirmation (D119): the repo git now has under the configured name is the agent repo, and one reconcile
/// pass runs without the removal brake. For a deliberate move or restore, never automatic. Owner only.
/// </summary>
public sealed record ConfirmRepoCommand(StudioUser User) : IRequest<Outcome<StudioResult>>;
