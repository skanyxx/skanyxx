namespace Skanyxx.Core.Platform.Studio;

/// <param name="Url">Where the run's log is, as git reported it; null when none.</param>
public sealed record CheckRun(string Name, CheckState State, string Description, string? Url);
