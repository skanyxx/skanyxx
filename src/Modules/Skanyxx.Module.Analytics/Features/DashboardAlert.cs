namespace Skanyxx.Module.Analytics.Features;

/// <summary>Something needing attention: <paramref name="Severity"/> warn or info, and the page that deals with it.</summary>
public sealed record DashboardAlert(string Severity, string Text, string? Url);
