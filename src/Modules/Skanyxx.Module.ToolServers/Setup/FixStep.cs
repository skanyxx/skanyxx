namespace Skanyxx.Module.ToolServers.Setup;

/// <summary>One numbered step: what to do, and the command that does it when there is one (shape of the shared setup guide's step).</summary>
public sealed record FixStep(string Text, string? Command = null);
