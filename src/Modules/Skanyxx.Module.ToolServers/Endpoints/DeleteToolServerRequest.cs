namespace Skanyxx.Module.ToolServers.Endpoints;

internal sealed class DeleteToolServerRequest
{
    public string Namespace { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Query <c>?confirmInUse=true</c>: delete although agents use the server.</summary>
    public bool ConfirmInUse { get; set; }
}
