using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

/// <summary>
/// Only flips an active task: suspend a Pending/Running one, resume a Suspended one. AX would otherwise bring a Failed
/// or Terminating task back to life here without the caps, so any other phase is a 409 — re-run it with PUT instead.
/// </summary>
internal sealed class SetTaskSuspendedHandler(AxGateway ax, KeyedLock locks, ILogger<SetTaskSuspendedHandler> logger)
    : IRequestHandler<SetTaskSuspendedCommand, Outcome<SandboxTask>>
{
    public Task<Outcome<SandboxTask>> Handle(SetTaskSuspendedCommand command, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, () => locks.RunAsync(KeyedLock.ForTask(command.Name), async () =>
        {
            var task = await ax.FindTaskAsync(command.Name, ct);
            if (task is null)
                return Outcome<SandboxTask>.NotFound($"No task '{command.Name}'.");
            if (!Ownership.CanManage(command.IsSupervisor, command.UserId!, TaskEnv.Read(task, TaskEnv.Owner)))
                return Outcome<SandboxTask>.Forbidden("Only the person who started the task, or a supervisor, may suspend or resume it.");
            if (!TaskPhases.CanSetSuspended(task.Status?.Phase ?? "", command.Suspend))
                return Outcome<SandboxTask>.Conflict(default, command.Suspend
                    ? "Only a pending or running task can be suspended."
                    : "Only a suspended task can be resumed; re-run a failed or stopped one with PUT.");

            return Outcome<SandboxTask>.Ok(AxMapper.ToSandboxTask(await ax.SetSuspendedAsync(command.Name, command.Suspend, ct)));
        }, ct));
}
