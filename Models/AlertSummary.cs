namespace SkanyxxMaui.Models;

public class AlertSummary
{
    public int TotalAlerts { get; set; }
    public int FiringAlerts { get; set; }
    public int ResolvedAlerts { get; set; }
    public AlertSeverityBreakdown? SeverityBreakdown { get; set; }
    public AlertEventTypeBreakdown? EventTypeBreakdown { get; set; }
}
