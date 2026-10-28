namespace Skanyxx.Host.Setup;

/// <summary>One numbered step of a guide: what to do, and the command that does it when there is one.</summary>
public sealed record SetupStep(string Text, string? Command = null);
