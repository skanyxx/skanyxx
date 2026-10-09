namespace Skanyxx.Core.Platform.Studio;

/// <summary>An agent in main, and whether kagent runs it as merged.</summary>
public sealed record StudioAgentSummary(string Name, string Description, bool Live);
