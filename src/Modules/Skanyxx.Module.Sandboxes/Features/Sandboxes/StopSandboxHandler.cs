using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

/// <summary>
/// Stop = AX <c>DeleteTask</c>. The sandbox's memory token is revoked first (D076): a token copied out of the sandbox
/// must die with it, and a failed delete then leaves a sandbox without memory rather than a stopped one with it.
/// </summary>
internal sealed class StopSandboxHandler(AxGateway ax, KeyedLock locks, MemoryAccess memory, ILogger<StopSandboxHandler> logger)
    : IRequestHandler<StopSandboxCommand, Outcome<Sandbox>>
{
    public Task<Outcome<Sandbox>> Handle(StopSandboxCommand command, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, () => locks.RunAsync(KeyedLock.ForSandbox(command.Name), async () =>
        {
            var sandbox = await ax.FindSandboxAsync(command.Name, ct);
            if (sandbox is null)
                return Outcome<Sandbox>.NotFound($"No sandbox '{command.Name}'.");
            if (!Ownership.CanManage(command.IsSupervisor, command.UserId!, SandboxEnv.Read(sandbox, SandboxEnv.Owner)))
                return Outcome<Sandbox>.Forbidden("Only the person who started the sandbox, or a supervisor, may stop it.");

            if (await memory.RevokeAsync(SandboxEnv.Read(sandbox, SandboxEnv.AgentId), ct) is { } refused)
                return new Outcome<Sandbox>(refused.Status, Message: refused.Message);
            await ax.DeleteSandboxAsync(command.Name, ct);
            return Outcome<Sandbox>.Accepted(AxMapper.ToSandbox(sandbox));
        }, ct));
}
