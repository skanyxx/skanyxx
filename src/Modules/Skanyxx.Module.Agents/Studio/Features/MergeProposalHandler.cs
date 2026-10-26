using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// The only way an agent becomes live (D024, D045): a supervisor's merge of files that pass the studio's checks at the
/// PR's head, pinned to that head, then the reconciler applies it at once. A grant that opens (or closes) a team or
/// department, in the proposal, in main or in memory, needs the owner (D091). Builders get 403 whatever the files say.
/// Once git has merged, nothing the person does (closing the page) cancels the apply, and nothing that goes wrong in it
/// is reported as a failed merge: it is 202, and the periodic pass finishes it (m1, m2). A plugin proposal (P8) is merged
/// by the same gate and applied by a whole pass: its servers, then its agents, then what it removed (under the brake).
/// </summary>
internal sealed class MergeProposalHandler(
    IAgentRepo repo, StudioRepoGuard guard, ProposalInspector inspector, StudioReconciler reconciler, IOptions<StudioOptions> options,
    ILogger<MergeProposalHandler> logger)
    : IRequestHandler<MergeProposalCommand, Outcome<StudioResult>>
{
    public async Task<Outcome<StudioResult>> Handle(MergeProposalCommand command, CancellationToken ct)
    {
        var user = command.User;
        if (!user.CanMerge)
        {
            logger.LogWarning("Studio merge of #{Number} refused for {ActorUserId}: not a supervisor", command.Number, user.UserId);
            return Outcome<StudioResult>.Forbidden(StudioFailure.NotSupervisor);
        }
        if (!options.Value.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured);
        AgentDraft? draft = null;
        RepoPull pull;
        string? plugin;
        bool removal;
        try
        {
            if (await repo.PullAsync(command.Number, ct) is not { Open: true } open)
                return Outcome<StudioResult>.NotFound(StudioFailure.NoProposal);
            pull = open;
            await guard.RequireAsync(setup: false, ct);
            var inspected = await inspector.InspectAsync(pull, ct);
            plugin = inspected.Plugin;
            removal = inspected.Removal;
            if (!inspected.Valid || (plugin is null && inspected.Draft is null))
                return StudioFailure.Conflict<StudioResult>($"This proposal cannot be merged: {string.Join(" ", inspected.Problems)}", "invalid");
            draft = inspected.Draft;
            if (inspected.NeedsOwner && !user.IsOwner)
            {
                logger.LogWarning("Studio merge of #{Number} ({Agent}) refused for {ActorUserId}: needs the owner", pull.Number,
                    plugin is null ? draft!.Name : $"plugin {plugin}", user.UserId);
                return Outcome<StudioResult>.Forbidden(plugin is null
                    ? "Team and department grants are the owner's (D091): only the owner may merge this proposal."
                    : StudioFailure.PluginNeedsOwner);
            }

            switch (await MergeAsync(pull, user))
            {
                case MergeOutcome.HeadMoved:
                    return StudioFailure.Conflict<StudioResult>("The proposal changed after it was read; review it again.", "stale");
                case MergeOutcome.NotMergeable:
                    return StudioFailure.Conflict<StudioResult>("git cannot merge this proposal (already merged, closed, or in conflict with main).", "unmergeable");
            }
        }
        catch (StudioRepoException ex)
        {
            return StudioFailure.Conflict<StudioResult>(ex.Message, StudioFailure.RepoReason);
        }
        catch (StudioApplyException ex)
        {
            return StudioFailure.Conflict<StudioResult>(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio merge of #{Number} failed", command.Number);
            return Outcome<StudioResult>.Unavailable(StudioFailure.Unavailable);
        }
        logger.LogWarning("Studio proposal #{Number} ({Agent}) merged by {ActorUserId} at {HeadSha}", pull.Number, plugin is null ? draft!.Name : $"plugin {plugin}",
            user.UserId, pull.HeadSha);
        if (plugin is not null)
            return await ApplyPluginAsync(pull, plugin, removal);

        bool live;
        try
        {
            live = await reconciler.ApplyMergedAsync(draft!, pull.Number, user.UserId!, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Merged #{Number} but applying {Agent} failed; the reconciler retries", pull.Number, draft!.Name);
            return Outcome<StudioResult>.Accepted(new StudioResult(
                $"Merged #{pull.Number}. It is not live yet ({Why(ex)}); the reconciler applies it on its next pass.", pull.Number, draft.Name));
        }
        // A suspended agent stays out of kagent (D121) whatever main says: say so instead of "live" (m3).
        return Outcome<StudioResult>.Ok(new StudioResult(live
            ? $"Merged #{pull.Number}: {draft!.Name} is live in kagent and in Chat."
            : $"Merged #{pull.Number}; {draft!.Name} is suspended, so it is not running. The owner resumes it.", pull.Number, draft.Name));
    }

    /// <summary>
    /// Like an agent's apply: whatever goes wrong after git merged is 202, and the periodic pass finishes it. Removals the
    /// brake held wait for the owner's confirmation, and servers still in use wait for a person to change their agents,
    /// not for a retry; the answer says so.
    /// </summary>
    private async Task<Outcome<StudioResult>> ApplyPluginAsync(RepoPull pull, string plugin, bool removal)
    {
        ReconcileResult result;
        try
        {
            result = await reconciler.ApplyAllAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Merged #{Number} but applying the plugin {Plugin} failed; the reconciler retries", pull.Number, plugin);
            return Outcome<StudioResult>.Accepted(new StudioResult(
                $"Merged #{pull.Number}. The plugin {plugin} is not {(removal ? "removed" : "applied")} yet ({Why(ex)}); the reconciler finishes on its next pass.",
                pull.Number, null));
        }
        var waiting = new List<string>();
        if (result.Braked > 0)
            waiting.Add($"{result.Braked} removal(s) wait for the owner's confirmation (Studio: the removal brake held them)");
        if (result.Failures > 0)
            waiting.Add($"{result.Failures} change(s) are not applied yet; the reconciler retries them on its next pass");
        if (result.KeptInUse.Count > 0)
            waiting.Add($"kept: in use — the server(s) {string.Join(", ", result.KeptInUse)} stay while agents in main still use them; change those agents first");
        if (waiting.Count > 0)
            return Outcome<StudioResult>.Accepted(new StudioResult($"Merged #{pull.Number}. {string.Join("; ", waiting)}.", pull.Number, null));
        return Outcome<StudioResult>.Ok(new StudioResult(removal
            ? $"Merged #{pull.Number}: the plugin {plugin} is removed (its agents, then its servers)."
            : $"Merged #{pull.Number}: the plugin {plugin} is applied. A server still missing a header value, and the agents using it, wait for the owner (Plugins).",
            pull.Number, null));
    }

    private static string Why(Exception ex) => ex switch
    {
        StudioApplyException => ex.Message,
        TimeoutException => "kagent, memory or another studio change did not answer in time",
        _ => "kagent or memory did not take it yet"
    };

    /// <summary>
    /// Once sent, the merge is not the person's to cancel (it has git's own deadline), and a merge whose answer was lost
    /// may still have happened: git is asked again before it counts as failed (m1, m2).
    /// </summary>
    private async Task<MergeOutcome> MergeAsync(RepoPull pull, StudioUser user)
    {
        try
        {
            return await repo.MergeAsync(pull.Number, pull.HeadSha, $"Merge #{pull.Number}: {pull.Title}", $"Merged-by: {user.UserId}\nHead: {pull.HeadSha}", CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            if (await repo.PullAsync(pull.Number, CancellationToken.None) is { Merged: true } after && after.HeadSha == pull.HeadSha)
            {
                logger.LogWarning(ex, "git's answer to the merge of #{Number} was lost, but it merged", pull.Number);
                return MergeOutcome.Merged;
            }
            throw;
        }
    }
}
