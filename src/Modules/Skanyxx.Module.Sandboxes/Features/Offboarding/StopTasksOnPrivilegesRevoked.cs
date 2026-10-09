using Grpc.Core;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Offboarding;

/// <summary>
/// D154: a disabled person's sandbox tasks stop — and, since D161, those of a person the Entra re-check refused
/// (<see cref="PrivilegesRevoked.AccessRemoved"/>). Every active task (Pending, Running, Suspended) whose recorded owner
/// is that person is deleted through AX — the module's "stop" — each under its task lock and re-read there, so a task
/// replaced or already stopped meanwhile is left alone, and one deleted between the read and the delete (another
/// replica, the person) counts as stopped (D163). Losing supervisor alone stops nothing: what supervisors manage is
/// decided per request (<see cref="Ownership"/>), and the person's own tasks stay theirs. Workspaces are storage, not
/// running code, and stay. AX unreachable, or a task that would not stop for any reason: the remaining tasks are still
/// tried, then this throws a <see cref="RevocationFailedException"/> saying so — identity answers <c>500</c> and
/// disabling the account again retries (every disable publishes). Runs after the local revocations (D160). With
/// sandboxes off it does nothing (and never builds the AX client).
/// </summary>
[NotificationOrder(NotificationOrderAttribute.External)]
internal sealed class StopTasksOnPrivilegesRevoked(
    IConfiguration configuration, IServiceProvider services, KeyedLock locks, ILogger<StopTasksOnPrivilegesRevoked> logger)
    : INotificationHandler<PrivilegesRevoked>
{
    public const string Unreachable = "Its sandbox tasks could not be stopped (AX unavailable)";

    private const int PageSize = 100;

    public async Task Handle(PrivilegesRevoked notification, CancellationToken ct)
    {
        if (!(notification.AccountDisabled || notification.AccessRemoved) || !SandboxesOptions.IsEnabled(configuration))
            return;

        var ax = services.GetRequiredService<AxGateway>();
        var user = notification.UserId;
        IReadOnlyList<string> owned;
        try
        {
            owned = await OwnedActiveAsync(ax, user, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("The sandbox tasks of {UserId} could not be listed: {Error}", user, Describe(ex));
            throw new RevocationFailedException(ex is InvalidOperationException ? $"Its sandbox tasks could not be stopped: {ex.Message}" : Unreachable + ".", ex);
        }

        var failed = 0;
        foreach (var name in owned)
        {
            try
            {
                var stopped = await locks.RunAsync(KeyedLock.ForTask(name), async () =>
                {
                    var task = await ax.FindTaskAsync(name, ct);
                    if (task is null || TaskEnv.Read(task, TaskEnv.Owner) != user || !TaskPhases.IsActive(task.Status?.Phase ?? ""))
                        return Outcome<bool>.Ok(false);
                    try
                    {
                        await ax.DeleteTaskAsync(name, ct);
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                    {
                        // Gone between the read and the delete: stopped is what was wanted.
                    }
                    return Outcome<bool>.Ok(true);
                }, ct);
                if (stopped.Status != OutcomeStatus.Ok)
                    throw new InvalidOperationException(stopped.Message);
                if (stopped.Value)
                    logger.LogWarning("Sandbox task {Task} of {UserId} stopped: {Reason}", name, user, notification.Reason);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failed++;
                logger.LogWarning("Sandbox task {Task} of {UserId} could not be stopped: {Error}", name, user, Describe(ex));
            }
        }

        if (failed > 0)
            throw new RevocationFailedException($"{failed} of its {owned.Count} sandbox tasks could not be stopped (AX unavailable or refused).");
    }

    private static string Describe(Exception ex) => ex is RpcException rpc ? rpc.StatusCode.ToString() : ex.GetType().Name + ": " + ex.Message;

    /// <summary>
    /// Pages until an empty page (AX drops records it cannot read, so a short page is not the end). Past
    /// <see cref="AxGateway.MaxCounted"/> tasks it cannot be sure it saw them all, so it refuses rather than stop some.
    /// </summary>
    private static async Task<IReadOnlyList<string>> OwnedActiveAsync(AxGateway ax, string user, CancellationToken ct)
    {
        var owned = new List<string>();
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await ax.ListTasksAsync(PageSize, offset, ct);
            if (page.Count == 0)
                return [.. owned.Distinct()];
            if (offset >= AxGateway.MaxCounted)
                throw new InvalidOperationException($"The atespace holds {AxGateway.MaxCounted} or more AX tasks: too many to find every task of {user}.");
            owned.AddRange(page.Where(t => TaskEnv.Read(t, TaskEnv.Owner) == user && TaskPhases.IsActive(t.Status?.Phase ?? ""))
                .Select(t => t.Metadata.Name));
        }
    }
}
