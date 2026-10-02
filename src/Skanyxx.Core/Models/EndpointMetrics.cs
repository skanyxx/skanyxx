namespace Skanyxx.Core.Models;

public class EndpointMetrics
{
    public string Endpoint { get; set; } = string.Empty;
    public long Requests { get; set; }
    public int AvgLatencyMs { get; set; }
    public decimal ErrorRate { get; set; }
    public int P99LatencyMs { get; set; }
}
