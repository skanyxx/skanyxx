namespace SkanyxxMaui.Models;

public class Alert
{
    public string Id { get; set; } = string.Empty;
    public string HookName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public AlertStatus Status { get; set; }
    public string FirstSeen { get; set; } = string.Empty;
    public string LastSeen { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string? TaskId { get; set; }
    public RemediationStatus? RemediationStatus { get; set; }
}
