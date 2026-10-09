using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Agents.Studio.Git;

namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>
/// D119: the studio trusts one agent repo, recorded (git's id and creation time) when it first creates or sees it. A
/// repo that is gone, or a different repo answering under the configured name (a lost git volume, a typo in
/// <c>Studio:Git</c>, another git server), is refused, never re-created: main of an empty repo would otherwise remove
/// every live agent. The repo and its main protection are created only while none was ever recorded (setup, and the
/// loop's retries of it); for the recorded repo every caller only verifies, so a protection rule that went missing or
/// changed is refused like a foreign repo (anything pushed meanwhile is not trusted), and a page view never writes to
/// git. The owner re-protects, or moves the studio to another repo, deliberately (<see cref="ConfirmAsync"/>).
/// </summary>
internal sealed class StudioRepoGuard(IAgentRepo repo, IServiceScopeFactory scopes, ILogger<StudioRepoGuard> logger)
{
    public const string ConfirmHint = "The owner restores it, or confirms the repo as it is now (which also re-creates a missing protection rule) with POST api/studio/confirm.";

    /// <param name="setup">
    /// The reconciler's pass: may create, protect and record the repo while none was ever recorded. Everyone else (overview,
    /// propose, preview, merge) only reads.
    /// </param>
    /// <exception cref="StudioRepoException">The repo is not the one the studio trusts, or not protected; logged at Error.</exception>
    public async Task RequireAsync(bool setup, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var recorded = Require(await mediator.Send(new StudioRepoQuery(), ct));
        var current = await repo.InfoAsync(ct);
        if (recorded is null)
        {
            if (!setup)
                throw new StudioRepoException($"The agent repo {repo.Name} is not set up yet: the reconciler creates or records it on its next pass.");
            // Created now, or first sight: setup created it before a record existed, or an install from before D119.
            current ??= await repo.CreateAsync(ct);
            await repo.ProtectAsync(current, ct);
            Require(await mediator.Send(new RecordStudioRepoCommand(StudioReconciler.Actor, current.Identity), ct));
            return;
        }
        if (current is null)
            throw Refuse($"The agent repo {repo.Name} (git id {recorded.Id}) is gone. The studio changes nothing in kagent "
                + $"or memory until it is restored. {ConfirmHint}");
        if (recorded.Id != current.Identity.Id || recorded.CreatedAt != current.Identity.CreatedAt)
            throw Refuse($"The agent repo {repo.Name} is not the one the studio recorded (git id {recorded.Id}, created {recorded.CreatedAt}; "
                + $"now git id {current.Identity.Id}, created {current.Identity.CreatedAt}). The studio changes nothing until the owner confirms it. {ConfirmHint}");
        try
        {
            await repo.VerifyAsync(current, ct);
        }
        catch (StudioRepoException ex)
        {
            throw Refuse(ex.Message);
        }
    }

    /// <summary>A person must act: loudly, whichever caller met it first (m1).</summary>
    private StudioRepoException Refuse(string reason)
    {
        logger.LogError("Studio refuses the agent repo {Repo}: {Reason}", repo.Name, reason);
        return new StudioRepoException(reason);
    }

    /// <summary>The owner's confirmation: whatever answers under the configured name now is the agent repo.</summary>
    public async Task<StudioRepoIdentity> ConfirmAsync(string actor, CancellationToken ct)
    {
        var current = await repo.InfoAsync(ct) ?? throw new StudioRepoException($"git has no repo {repo.Name} to confirm.");
        await repo.ProtectAsync(current, ct);
        using var scope = scopes.CreateScope();
        Require(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RecordStudioRepoCommand(actor, current.Identity), ct));
        logger.LogWarning("Studio agent repo confirmed by {ActorUserId}: {Repo} (git id {RepoId})", actor, repo.Name, current.Identity.Id);
        return current.Identity;
    }

    private static T Require<T>(Outcome<T> outcome) =>
        outcome.Status == OutcomeStatus.Ok ? outcome.Value! : throw new StudioApplyException(outcome.Message ?? "Memory refused the change.");
}
