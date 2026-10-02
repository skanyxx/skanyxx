namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>The only SSE <c>event:</c> names a watch writes; AX's own action text never reaches the wire.</summary>
public static class WatchEventKinds
{
    public const string Initial = "initial";
    public const string Modified = "modified";
    public const string Final = "final";
    public const string Gone = "gone";
    public const string Error = "error";

    /// <summary>Written as an SSE comment, not an event.</summary>
    public const string KeepAlive = "keepalive";
}
