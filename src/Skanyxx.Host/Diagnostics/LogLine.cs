namespace Skanyxx.Host.Diagnostics;

/// <summary>A warning or error as the Debug page shows it: the message already redacted, the exception by type and message only.</summary>
public sealed record LogLine(DateTimeOffset At, string Level, string Source, string Message, string? Exception);
