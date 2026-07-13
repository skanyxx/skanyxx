namespace SkanyxxWeb.Models;

public class CloudProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = "Connected";
    public Dictionary<string, int> ResourceCounts { get; set; } = new();
}
