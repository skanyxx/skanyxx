namespace SkanyxxWeb.Models;

public class AnalyticsData
{
    public long TotalEvents { get; set; }
    public int AvgResponseTimeMs { get; set; }
    public decimal SuccessRate { get; set; }
    public int ActiveServices { get; set; }
}
