namespace SkanyxxWeb.Models;

public class KAgentEvent
{
    public string Id { get; set; } = string.Empty;
    public object? Data { get; set; }
    public string Timestamp { get; set; } = string.Empty;
}
