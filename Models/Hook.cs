namespace SkanyxxWeb.Models;

public class Hook
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime? LastTriggered { get; set; }
}
