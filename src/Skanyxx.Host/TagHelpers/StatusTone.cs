namespace Skanyxx.Host.TagHelpers;

/// <summary>
/// The badge colour for a status word from kagent, AX, git or identity. One mapping for the whole UI (ui.js has the
/// same list for badges built in the browser), so "Ready" never reads green on one page and blue on another.
/// </summary>
public static class StatusTone
{
    private static readonly HashSet<string> Success = new(StringComparer.OrdinalIgnoreCase)
        { "ready", "accepted", "healthy", "running", "active", "live", "succeeded", "published", "merged", "enabled", "online", "ok", "set" };
    private static readonly HashSet<string> Warning = new(StringComparer.OrdinalIgnoreCase)
        { "pending", "starting", "creating", "draft", "degraded", "suspended", "paused", "waiting", "unknown", "expiring", "experimental" };
    private static readonly HashSet<string> Danger = new(StringComparer.OrdinalIgnoreCase)
        { "failed", "error", "unhealthy", "rejected", "refused", "invalid", "crashloopbackoff", "offline" };

    /// <summary>success, warning, danger or neutral.</summary>
    public static string Of(string? status) =>
        status is null ? "neutral"
        : Success.Contains(status) ? "success"
        : Warning.Contains(status) ? "warning"
        : Danger.Contains(status) ? "danger"
        : "neutral";
}
