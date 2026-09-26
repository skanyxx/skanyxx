using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;
using AxTask = Ax.V1Alpha1.Task;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

/// <summary>
/// Create-or-replace. Only the owner may replace a task (a supervisor only one made outside Skanyxx, and adopts it).
/// Any run that makes a task active — new, or Failed/Completed/Terminating before — counts against the per-user and
/// the atespace caps and gets a fresh memory agent id; replacing an active task keeps both its id and its suspension.
/// Activations count and upsert under one atespace-wide key (taken inside the task key), so parallel runs — whatever
/// user they claim — cannot all pass the caps; a replace that stays active does not take it.
/// </summary>
internal sealed class RunTaskHandler(AxGateway ax, KeyedLock locks, IOptions<SandboxesOptions> options, ILogger<RunTaskHandler> logger)
    : IRequestHandler<RunTaskCommand, Outcome<SandboxTask>>
{
    public Task<Outcome<SandboxTask>> Handle(RunTaskCommand command, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, () => locks.RunAsync(KeyedLock.ForTask(command.Name), () => RunAsync(command, ct), ct));

    private async Task<Outcome<SandboxTask>> RunAsync(RunTaskCommand command, CancellationToken ct)
    {
        var user = command.UserId!;
        var existing = await ax.FindTaskAsync(command.Name, ct);
        var owner = existing is null ? null : TaskEnv.Read(existing, TaskEnv.Owner);
        if (existing is not null && !Ownership.CanReplace(options.Value, user, owner))
            return Outcome<SandboxTask>.Forbidden(
                "Only the person who started the task may replace it; a supervisor may stop, suspend or resume it.");

        if (existing is not null && TaskPhases.IsActive(existing.Status?.Phase ?? ""))
            return await UpsertAsync(command, existing, owner == user ? TaskEnv.Read(existing, TaskEnv.AgentId) : null, ct);

        return await locks.RunAsync(KeyedLock.Atespace, async () =>
            await RefuseAsync(user, ct) ?? await UpsertAsync(command, existing, null, ct), ct);
    }

    private async Task<Outcome<SandboxTask>> UpsertAsync(RunTaskCommand command, AxTask? existing, string? keptAgentId, CancellationToken ct)
    {
        var saved = await ax.UpdateTaskAsync(AxMapper.ToAxTask(command.Name, command.UserId!, keptAgentId ?? TaskEnv.NewAgentId(command.Name),
            command.Image, command.Command, command.Env, command.Workspaces, ResourcePolicy.Apply(command.Resources, options.Value),
            existing?.Spec?.Suspend ?? false), ct);
        var task = AxMapper.ToSandboxTask(saved);
        return existing is null ? Outcome<SandboxTask>.Accepted(task) : Outcome<SandboxTask>.Ok(task);
    }

    /// <summary>Null when the caps leave room. A count that cannot finish refuses the run rather than guess.</summary>
    private async Task<Outcome<SandboxTask>?> RefuseAsync(string user, CancellationToken ct)
    {
        var settings = options.Value;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(settings.CountBudgetSeconds));

        ActiveTaskCount? count;
        try
        {
            count = await ax.CountActiveAsync(user, budget.Token);
        }
        catch (Exception ex) when (AxErrors.IsCancelled(ex, budget.Token) && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Counting active AX tasks took over {Seconds}s (Sandboxes:CountBudgetSeconds); refusing the run.",
                settings.CountBudgetSeconds);
            return Outcome<SandboxTask>.Unavailable(AxErrors.Unreachable);
        }

        if (count is null)
        {
            logger.LogWarning("The atespace holds {Max} or more AX tasks, too many to count against the caps; refusing the run.",
                AxGateway.MaxCounted);
            return Outcome<SandboxTask>.Unavailable("Too many AX tasks to enforce the task caps; refusing the run.");
        }
        if (count.Mine >= settings.MaxActiveTasksPerUser)
            return Outcome<SandboxTask>.RateLimited(
                $"You already have {count.Mine} active AX tasks (Sandboxes:MaxActiveTasksPerUser); stop one first.");
        if (count.All >= settings.MaxActiveTasks)
            return Outcome<SandboxTask>.RateLimited("The atespace is at its active-task limit (Sandboxes:MaxActiveTasks); try again later.");
        return null;
    }
}
