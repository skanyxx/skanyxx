namespace Skanyxx.Module.Agents.Studio.Git;

internal enum MergeOutcome
{
    Merged,

    /// <summary>The branch moved after it was validated: nothing was merged.</summary>
    HeadMoved,

    /// <summary>Already merged or closed, or git refuses (conflict with main).</summary>
    NotMergeable
}
