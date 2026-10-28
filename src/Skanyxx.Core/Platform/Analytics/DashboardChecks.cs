namespace Skanyxx.Core.Platform.Analytics;

/// <summary>
/// The health checks the Dashboard reads, by name. Each module registers its own only when that dependency is configured
/// on this install (absence means "off", not "down"), tagged <see cref="HealthEndpoint.DiagnosticsTag"/> so an optional
/// dependency never takes the Host out of rotation. Descriptions are fixed texts; the Dashboard shows status and data only.
/// </summary>
public static class DashboardChecks
{
    public const string PostgresSuffix = "-postgres";
    public const string AnswerCache = "answer-cache";
    public const string StudioGit = "studio-git";
    public const string StudioReconciler = "studio-reconciler";
    public const string Sandboxes = "sandboxes-ax";

    /// <summary>The reconciler check's data: the last pass (ISO-8601 UTC) and how many agents it left unapplied.</summary>
    public const string LastPassKey = "lastPass";
    public const string FailuresKey = "failures";
}
