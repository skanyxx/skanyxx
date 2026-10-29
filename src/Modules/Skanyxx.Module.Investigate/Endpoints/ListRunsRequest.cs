using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Endpoints;

public sealed class ListRunsRequest
{
    public Guid? WorkflowId { get; set; }

    public RunState? State { get; set; }

    public bool Mine { get; set; }

    public string? Q { get; set; }

    public int Limit { get; set; } = 100;
}
