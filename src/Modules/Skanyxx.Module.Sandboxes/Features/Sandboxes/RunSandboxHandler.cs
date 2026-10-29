using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;
using AxTask = Ax.V1Alpha1.Task;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

/// <summary>
/// Create-or-replace. Only the owner may replace a sandbox (a supervisor only one made outside Skanyxx, and adopts it).
/// Any run that makes a sandbox active — new, or Failed/Completed/Terminating before — counts against the per-user and
/// the atespace caps and gets a fresh memory agent id; replacing an active sandbox keeps both its id and its suspension.
/// Activations count and upsert under one atespace-wide key (taken inside the sandbox key), so parallel runs — whatever
/// user they claim — cannot all pass the caps; a replace that stays active does not take it.
/// With memory attach on (D076/D077), a run that binds a workspace with memory attached gets a freshly signed token for
/// its agent id in its env (a replace re-issues it, so the previous one stops working); a run without one gets none, and
/// the previous activation's token is revoked first either way — before AX is told, so a failed save fails closed.
/// Under the sandbox lock, just before AX is told, the caller is checked again against identity (D395): a request let in
/// before its maker's role was saved away must not start a sandbox the revocation has already swept for. The sweep lists AX
/// without this lock, so a demotion that commits during the AX call is missed by both checks; identity is therefore asked
/// once more after AX answers, still under the lock, and a maker who lost the role gets the sandbox (and its memory token)
/// deleted again (D526).
/// </summary>
internal sealed class RunSandboxHandler(
    AxGateway ax, KeyedLock locks, MemoryAccess memory, IPersonStanding standing, IOptions<SandboxesOptions> options, ILogger<RunSandboxHandler> logger)
    : IRequestHandler<RunSandboxCommand, Outcome<Sandbox>>
{
    private static readonly string[] Runners = [SkanyxxRoles.Owner, SkanyxxRoles.Supervisor];
    private static readonly Outcome<Sandbox> NoLongerRuns = Outcome<Sandbox>.Forbidden("Your account may no longer run sandboxes.");

    public Task<Outcome<Sandbox>> Handle(RunSandboxCommand command, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, () => locks.RunAsync(KeyedLock.ForSandbox(command.Name), () => RunAsync(command, ct), ct));

    private async Task<Outcome<Sandbox>> RunAsync(RunSandboxCommand command, CancellationToken ct)
    {
        var user = command.UserId!;
        var existing = await ax.FindSandboxAsync(command.Name, ct);
        var owner = existing is null ? null : SandboxEnv.Read(existing, SandboxEnv.Owner);
        if (existing is not null && !Ownership.CanReplace(user, owner))
            return Outcome<Sandbox>.Forbidden(
                "Only the person who started the sandbox may replace it; a supervisor may stop, suspend or resume it.");

        if (existing is not null && SandboxPhases.IsActive(existing.Status?.Phase ?? ""))
            return await UpsertAsync(command, existing, owner == user ? SandboxEnv.Read(existing, SandboxEnv.AgentId) : null, ct);

        return await locks.RunAsync(KeyedLock.Atespace, async () =>
            await RefuseAsync(user, ct) ?? await UpsertAsync(command, existing, null, ct), ct);
    }

    private async Task<Outcome<Sandbox>> UpsertAsync(RunSandboxCommand command, AxTask? existing, string? keptAgentId, CancellationToken ct)
    {
        if (!await MayRunAsync(command.UserId!, ct))
            return NoLongerRuns;
        var agentId = keptAgentId ?? SandboxEnv.NewAgentId(command.Name);
        (string Url, string Token)? access = null;
        if (memory.On)
        {
            var wantsMemory = await BindsMemoryAsync(command.Workspaces, ct);
            var previous = existing is null ? null : SandboxEnv.Read(existing, SandboxEnv.AgentId);
            if ((previous != agentId || !wantsMemory) && await memory.RevokeAsync(previous, ct) is { } refused)
                return new Outcome<Sandbox>(refused.Status, Message: refused.Message);
            if (wantsMemory)
            {
                var issued = await memory.IssueAsync(command.Name, agentId, command.UserId!, ct);
                if (issued.Status != OutcomeStatus.Ok)
                    return new Outcome<Sandbox>(issued.Status, Message: issued.Message);
                access = (memory.Url, issued.Value!.Token);
            }
        }

        var saved = await ax.UpdateSandboxAsync(AxMapper.ToAxTask(command.Name, command.UserId!, agentId,
            command.Image, command.Command, command.Env, command.Workspaces, ResourcePolicy.Apply(command.Resources, options.Value),
            existing?.Spec?.Suspend ?? false, access), ct);
        // Not the caller's token: a client gone now must not leave a demoted maker's sandbox running.
        if (!await MayRunAsync(command.UserId!, CancellationToken.None))
            return await UndoAsync(command.Name, agentId);
        var sandbox = AxMapper.ToSandbox(saved);
        return existing is null ? Outcome<Sandbox>.Accepted(sandbox) : Outcome<Sandbox>.Ok(sandbox);
    }

    private async Task<bool> MayRunAsync(string user, CancellationToken ct) =>
        (await standing.ActiveRolesAsync(user, ct)).Overlaps(Runners);

    /// <summary>The revocation sweep may have listed AX before this sandbox existed: stop it as the sweep would have.</summary>
    private async Task<Outcome<Sandbox>> UndoAsync(string name, string agentId)
    {
        logger.LogWarning("Sandbox {Sandbox} was started as its maker lost the right to run sandboxes; stopping it.", name);
        await memory.RevokeAsync(agentId, CancellationToken.None);
        await ax.DeleteSandboxAsync(name, CancellationToken.None);
        return NoLongerRuns;
    }

    /// <summary>Whether any workspace the run binds carries the <c>skanyxx-memory</c> MCP entry (a supervisor's attachMemory).</summary>
    private async Task<bool> BindsMemoryAsync(IReadOnlyList<WorkspaceMount> mounts, CancellationToken ct)
    {
        if (mounts.Count == 0)
            return false;
        var bound = mounts.Select(m => m.Name).ToHashSet();
        return (await ax.ListWorkspacesAsync(ct))
            .Any(w => bound.Contains(w.Metadata?.Name ?? "") && AxMapper.ToSandboxWorkspace(w).MemoryAttached);
    }

    /// <summary>Null when the caps leave room. A count that cannot finish refuses the run rather than guess.</summary>
    private async Task<Outcome<Sandbox>?> RefuseAsync(string user, CancellationToken ct)
    {
        var settings = options.Value;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(settings.CountBudgetSeconds));

        ActiveSandboxCount? count;
        try
        {
            count = await ax.CountActiveAsync(user, budget.Token);
        }
        catch (Exception ex) when (AxErrors.IsCancelled(ex, budget.Token) && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Counting active sandboxes took over {Seconds}s (Sandboxes:CountBudgetSeconds); refusing the run.",
                settings.CountBudgetSeconds);
            return Outcome<Sandbox>.Unavailable(AxErrors.Unreachable);
        }

        if (count is null)
        {
            logger.LogWarning("The atespace holds {Max} or more sandboxes, too many to count against the caps; refusing the run.",
                AxGateway.MaxCounted);
            return Outcome<Sandbox>.Unavailable("Too many sandboxes to enforce the sandbox caps; refusing the run.");
        }
        if (count.Mine >= settings.MaxActiveTasksPerUser)
            return Outcome<Sandbox>.RateLimited(
                $"You already have {count.Mine} active sandboxes (Sandboxes:MaxActiveTasksPerUser); stop one first.");
        if (count.All >= settings.MaxActiveTasks)
            return Outcome<Sandbox>.RateLimited("The atespace is at its active-sandbox limit (Sandboxes:MaxActiveTasks); try again later.");
        return null;
    }
}
