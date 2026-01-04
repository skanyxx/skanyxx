namespace SkanyxxMaui.Models;

public class CreateMemoryRequest
{
    public string Ref { get; set; } = string.Empty;
    public MemoryProvider Provider { get; set; } = new();
    public string ApiKey { get; set; } = string.Empty;
    public PineconeParams? PineconeParams { get; set; }
}
