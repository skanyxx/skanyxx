using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// The owner adds the CI workflow (D362) to the agent repo: one pull request holding exactly the shipped workflow file,
/// merged at once by Skanyxx's account (main takes no direct pushes). Owner only: it is not an agent change, so no
/// supervisor review applies to it.
/// </summary>
public sealed record InstallCiCommand(StudioUser User) : IRequest<Outcome<StudioResult>>;
