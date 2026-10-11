using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// D076: signs the memory token for one activation of a sandbox. It binds the sandbox, its memory agent id and its owner,
/// and replaces any earlier token of that agent id. Sent by the sandboxes module just before it saves the sandbox in AX.
/// </summary>
public sealed record IssueSandboxMemoryTokenCommand(string SandboxName, string AgentId, string OwnerId)
    : IRequest<Outcome<SandboxMemoryToken>>;
