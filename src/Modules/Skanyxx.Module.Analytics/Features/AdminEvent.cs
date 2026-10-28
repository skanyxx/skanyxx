namespace Skanyxx.Module.Analytics.Features;

/// <summary>An audit row as the Dashboard shows it: when, what, and who (email or id). No details and no address.</summary>
public sealed record AdminEvent(DateTimeOffset At, string Action, string? Actor, string? Target);
