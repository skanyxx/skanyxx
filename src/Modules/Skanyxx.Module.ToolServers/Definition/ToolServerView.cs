using Skanyxx.Module.ToolServers.Setup;

namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>
/// One server on the page. <see cref="Url"/>, <see cref="Protocol"/>, <see cref="TimeoutSeconds"/> and
/// <see cref="Headers"/> are known only for servers created here (kagent does not list them). <see cref="Tools"/> and
/// <see cref="UsedBy"/> are null when kagent could not be asked. <see cref="Fixes"/>: what is not working, first one first.
/// </summary>
public sealed record ToolServerView(
    string Ref,
    string Namespace,
    string Name,
    string Kind,
    string KindLabel,
    bool Supported,
    string Source,
    string? Url,
    string? Protocol,
    int? TimeoutSeconds,
    string? Description,
    IReadOnlyList<HeaderView> Headers,
    IReadOnlyList<ToolView>? Tools,
    IReadOnlyList<string>? UsedBy,
    ServerStatus Status,
    IReadOnlyList<SetupFix> Fixes,
    bool CanEdit,
    bool CanDelete);
