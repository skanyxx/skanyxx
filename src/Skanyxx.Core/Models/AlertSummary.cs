namespace Skanyxx.Core.Models;

public class AlertSummary
{
    public int Total { get; set; }
    public int Firing { get; set; }
    public int Acknowledged { get; set; }
    public int Resolved { get; set; }
    public AlertSeverityBreakdown BySeverity { get; set; } = new();
    public AlertEventTypeBreakdown ByEventType { get; set; } = new();
}

public class AlertSeverityBreakdown
{
    public int Critical { get; set; }
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
}

public class AlertEventTypeBreakdown
{
    public int PodRestart { get; set; }
    public int PodPending { get; set; }
    public int OomKill { get; set; }
    public int ProbeFailed { get; set; }
}
