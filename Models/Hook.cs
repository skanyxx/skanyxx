namespace SkanyxxMaui.Models;

public class Hook
{
    public string ApiVersion { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public HookMetadata Metadata { get; set; } = new();
    public HookSpec Spec { get; set; } = new();
    public HookStatus? Status { get; set; }
}
