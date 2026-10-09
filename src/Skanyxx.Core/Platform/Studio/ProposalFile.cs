namespace Skanyxx.Core.Platform.Studio;

/// <summary>A file a proposal adds or changes, as the supervisor reviews it.</summary>
public sealed record ProposalFile(string Path, string Status, string? Content);
