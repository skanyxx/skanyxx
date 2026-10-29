namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// One change to the agent repo as people follow it (D365): its pull request (with a link to git), the CI checks of its
/// head, whether it merged, and where the agent is in kagent since.
/// </summary>
/// <param name="Url">The pull request in git; empty when git gave none.</param>
/// <param name="Deploy">Only for a merged agent change.</param>
public sealed record StudioChange(
    int Number, string Title, string? Agent, string Url, string? ProposedBy, ChangeStatus Status, ChangeChecks Checks, DeployStatus? Deploy,
    IReadOnlyList<ChangeStep> Steps);
