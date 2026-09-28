using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

/// <summary>Create-or-update (AX <c>UpdateTask</c>). A new task is 202: AX starts it asynchronously.</summary>
public sealed record RunTaskCommand(
    string? UserId,
    bool IsSupervisor,
    string Name,
    string Image,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<WorkspaceMount> Workspaces,
    SandboxResources? Resources) : IRequest<Outcome<SandboxTask>>
{
    public const int MaxCommandArgs = 64;
    public const int MaxEnv = 64;
    public const int MaxWorkspaces = 8;
    public const int MaxValueChars = 8_192;
    public const int MaxGoalChars = 4_000;
}
