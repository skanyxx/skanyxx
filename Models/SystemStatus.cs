namespace SkanyxxWeb.Models;

public class SystemStatus
{
    public string ServiceName { get; set; } = string.Empty;
    public string Status { get; set; } = "Online";
    public int LatencyMs { get; set; }
}
