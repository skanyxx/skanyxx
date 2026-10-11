using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

/// <summary>Create-or-update (AX <c>UpdateTask</c>). A new sandbox is 202: AX starts it asynchronously.</summary>
public sealed record RunSandboxCommand(
    string? UserId,
    bool IsSupervisor,
    string Name,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<WorkspaceMount> Workspaces,
    SandboxResources? Resources) : IRequest<Outcome<Sandbox>>
{
    public const int MaxCommandArgs = 64;
    public const int MaxEnv = 64;
    public const int MaxWorkspaces = 8;
    public const int MaxValueChars = 8_192;
    public const int MaxGoalChars = 4_000;

    /// <summary>
    /// Literal routes beside <c>/api/sandboxes/{name}</c> (D179): a sandbox with one of these names could not be read
    /// back, because <c>GET /api/sandboxes/workspaces</c> is the workspace list.
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedNames = ["workspaces", "models", "tasks"];
}
