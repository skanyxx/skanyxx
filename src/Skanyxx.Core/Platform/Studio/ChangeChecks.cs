namespace Skanyxx.Core.Platform.Studio;

/// <summary>Every check git reports for a commit, and what they add up to (any failure fails, any pending is pending).</summary>
public sealed record ChangeChecks(CheckState State, IReadOnlyList<CheckRun> Runs)
{
    public static ChangeChecks None { get; } = new(CheckState.None, []);
}
