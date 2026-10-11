using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;
using AxTask = Ax.V1Alpha1.Task;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

/// <summary>
/// Only flips an active sandbox: suspend a Pending/Running one, resume a Suspended one. AX would otherwise bring a Failed
/// or Terminating sandbox back to life here without the caps, so any other phase is a 409 — re-run it with PUT instead.
/// A resume renews the sandbox's memory token (D076) when it carries one: the token lives
/// <c>Memory:SandboxTokenHours</c> from its issue, so a sandbox suspended past that would otherwise wake up without
/// memory. Same agent id and owner; the new token replaces the old row, and is in the env before AX resumes the pod.
/// </summary>
internal sealed class SetSandboxSuspendedHandler(AxGateway ax, KeyedLock locks, MemoryAccess memory, ILogger<SetSandboxSuspendedHandler> logger)
    : IRequestHandler<SetSandboxSuspendedCommand, Outcome<Sandbox>>
{
    public Task<Outcome<Sandbox>> Handle(SetSandboxSuspendedCommand command, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, () => locks.RunAsync(KeyedLock.ForSandbox(command.Name), async () =>
        {
            var sandbox = await ax.FindSandboxAsync(command.Name, ct);
            if (sandbox is null)
                return Outcome<Sandbox>.NotFound($"No sandbox '{command.Name}'.");
            if (!Ownership.CanManage(command.IsSupervisor, command.UserId!, SandboxEnv.Read(sandbox, SandboxEnv.Owner)))
                return Outcome<Sandbox>.Forbidden("Only the person who started the sandbox, or a supervisor, may suspend or resume it.");
            if (!SandboxPhases.CanSetSuspended(sandbox.Status?.Phase ?? "", command.Suspend))
                return Outcome<Sandbox>.Conflict(default, command.Suspend
                    ? "Only a pending or running sandbox can be suspended."
                    : "Only a suspended sandbox can be resumed; re-run a failed or stopped one with PUT.");

            if (!command.Suspend && await RenewMemoryTokenAsync(sandbox, ct) is { } refused)
                return refused;
            return Outcome<Sandbox>.Ok(AxMapper.ToSandbox(await ax.SetSuspendedAsync(command.Name, command.Suspend, ct)));
        }, ct));

    /// <summary>Null when renewed, or when the sandbox has no token to renew; otherwise memory's refusal (nothing resumed).</summary>
    private async Task<Outcome<Sandbox>?> RenewMemoryTokenAsync(AxTask sandbox, CancellationToken ct)
    {
        var agentId = SandboxEnv.Read(sandbox, SandboxEnv.AgentId);
        var owner = SandboxEnv.Read(sandbox, SandboxEnv.Owner);
        if (!memory.On || SandboxEnv.Read(sandbox, SandboxEnv.MemoryToken) is null || agentId is null || owner is null)
            return null;

        var issued = await memory.IssueAsync(sandbox.Metadata.Name, agentId, owner, ct);
        if (issued.Status != OutcomeStatus.Ok)
            return new Outcome<Sandbox>(issued.Status, Message: issued.Message);
        // The spec as AX holds it (still suspended), with only the token changed; the status is AX's to write.
        var renewed = sandbox.Clone();
        renewed.Status = null;
        renewed.Spec.Env.First(e => e.Name == SandboxEnv.MemoryToken).Value = issued.Value!.Token;
        await ax.UpdateSandboxAsync(renewed, ct);
        return null;
    }
}
