namespace SkanyxxWeb.Models;

public class HookExecution
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string HookName { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
