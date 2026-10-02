namespace Skanyxx.Module.Sandboxes.Endpoints.Tasks;

public sealed class ListTasksRequest
{
    public int Limit { get; set; } = 50;
    public int Offset { get; set; }
}
