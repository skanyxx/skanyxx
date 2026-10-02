using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

internal sealed class StopTaskHandler(AxGateway ax, KeyedLock locks, ILogger<StopTaskHandler> logger)
    : IRequestHandler<StopTaskCommand, Outcome<SandboxTask>>
{
    public Task<Outcome<SandboxTask>> Handle(StopTaskCommand command, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, () => locks.RunAsync(KeyedLock.ForTask(command.Name), async () =>
        {
            var task = await ax.FindTaskAsync(command.Name, ct);
            if (task is null)
                return Outcome<SandboxTask>.NotFound($"No task '{command.Name}'.");
            if (!Ownership.CanManage(command.IsSupervisor, command.UserId!, TaskEnv.Read(task, TaskEnv.Owner)))
                return Outcome<SandboxTask>.Forbidden("Only the person who started the task, or a supervisor, may stop it.");

            await ax.DeleteTaskAsync(command.Name, ct);
            return Outcome<SandboxTask>.Accepted(AxMapper.ToSandboxTask(task));
        }, ct));
}
