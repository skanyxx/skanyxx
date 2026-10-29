using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Git;

/// <summary>
/// What <see cref="StudioOptions.RequireChecks"/> waits for (D367, D374): the shipped workflow's own check, by its context
/// name (<c>validate-agents</c>, or Gitea Actions' <c>validate-agents / &lt;job&gt; (&lt;event&gt;)</c>), so a status anyone
/// with push access can post under another name never stands in for it. Any failed check still fails the change.
/// </summary>
internal static class RequiredCheck
{
    public const string Context = "validate-agents";

    public static bool IsRequired(string context) =>
        context == Context || context.StartsWith(Context + " /", StringComparison.Ordinal);

    /// <returns>Failed when any check failed; otherwise the state of the validate-agents runs (None while none reported).</returns>
    public static CheckState State(ChangeChecks checks) =>
        checks.State == CheckState.Failed
            ? CheckState.Failed
            : CheckStatusMap.Combine(checks.Runs.Where(r => IsRequired(r.Name)).Select(r => r.State));
}
