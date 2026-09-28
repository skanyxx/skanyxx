using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Workspaces;

/// <summary>
/// Create-or-replace (AX <c>UpdateWorkspace</c>). Supervisors only: workspaces are shared by every task that binds
/// them, so a repo or MCP endpoint changed here reaches other people's tasks. <c>AttachMemory</c> adds Skanyxx's
/// memory MCP server as <c>skanyxx-memory</c>.
/// </summary>
public sealed record SaveWorkspaceCommand(
    string? UserId, bool IsSupervisor, string Name, IReadOnlyList<GitSource> Git, IReadOnlyList<McpServerEntry> McpServers, bool AttachMemory)
    : IRequest<Outcome<SandboxWorkspace>>
{
    public const int MaxGit = 16;
    public const int MaxMcpServers = 16;
}
