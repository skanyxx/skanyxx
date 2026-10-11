namespace Skanyxx.Module.Sandboxes.Endpoints.Sandboxes;

public sealed class ListSandboxesRequest
{
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}
