namespace Skanyxx.Core.Platform.Studio;

/// <summary>One step of a change's timeline (D365): draft, pull request opened, checks, merged, deployed.</summary>
public sealed record ChangeStep(string Name, StepState State, string Detail);
