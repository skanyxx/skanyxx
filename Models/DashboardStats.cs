namespace SkanyxxWeb.Models;

public class DashboardStats
{
    public int ActiveAgents { get; set; }
    public decimal Uptime { get; set; }
    public int ActiveIncidents { get; set; }
    public int ApiCallsToday { get; set; }
    public string? AgentsChange { get; set; }
    public string? UptimeChange { get; set; }
    public string? IncidentsChange { get; set; }
    public string? ApiCallsChange { get; set; }
}

public class ServiceStatus
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "offline";
    public string Latency { get; set; } = "-";
}
