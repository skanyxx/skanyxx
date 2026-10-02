namespace Skanyxx.Core.Models;

public class ScaleRequest
{
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = "default";
    public int Replicas { get; set; }
}
