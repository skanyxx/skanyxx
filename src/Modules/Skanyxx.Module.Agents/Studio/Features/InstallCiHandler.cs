using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Ci;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// The owner's "Add CI to the agent repo" (D362): main is protected (no direct pushes, D110), so the shipped workflow goes
/// in as one pull request with that single file, merged pinned to its head by Skanyxx's account. Nothing else can ride
/// along: the content is <see cref="CiTemplate.Text"/>, the path <see cref="CiOptions.WorkflowPath"/>. Audited.
/// </summary>
internal sealed class InstallCiHandler(
    IAgentRepo repo, StudioRepoGuard guard, IMediator mediator, IOptions<StudioOptions> options, ILogger<InstallCiHandler> logger)
    : IRequestHandler<InstallCiCommand, Outcome<StudioResult>>
{
    public const string Trailer = "CI: validate-agents";

    public async Task<Outcome<StudioResult>> Handle(InstallCiCommand command, CancellationToken ct)
    {
        var user = command.User;
        if (!user.IsOwner)
            return Outcome<StudioResult>.Forbidden(StudioFailure.NotOwner);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured);
        var path = options.Value.Ci.WorkflowPath;
        var target = $"{repo.Name}:{path}";
        try
        {
            await guard.RequireAsync(setup: false, ct);
            var current = await repo.ReadAsync(path, options.Value.Git.Branch, ct);
            if (current == CiTemplate.Text)
                return StudioFailure.Conflict<StudioResult>("The agent repo already has this CI workflow.", "unchanged");
            if ((await repo.OpenPullsAsync(ct)).FirstOrDefault(p => p.Body.StartsWith(Trailer + "\n", StringComparison.Ordinal)) is { } pending)
                return StudioFailure.Conflict<StudioResult>($"A CI pull request is already open (#{pending.Number}).", "pending");

            await StudioAudit.WriteAsync(mediator, StudioAudit.CiInstallRequested, user, target, new { path, update = current is not null });
            var title = current is null ? "Add the agent CI workflow" : "Update the agent CI workflow";
            var pull = await repo.ProposeAsync($"studio/ci-{DateTime.UtcNow:yyyyMMddHHmmss}", [new RepoChange(path, CiTemplate.Text, current is not null)],
                $"{title}\n\nProposed-by: {user.UserId}", user.Name, user.Email, title,
                $"{Trailer}\nProposed-by: {user.UserId}\n\nThe workflow Skanyxx ships (docs/templates/agents-repo/validate-agents.yaml). Merged by the owner in Skanyxx.", ct);
            var merged = await repo.MergeAsync(pull.Number, pull.HeadSha, $"Merge #{pull.Number}: {title}", $"Merged-by: {user.UserId}\nHead: {pull.HeadSha}",
                CancellationToken.None);
            await StudioAudit.OutcomeAsync(mediator, logger, merged == MergeOutcome.Merged ? StudioAudit.CiInstalled : StudioAudit.CiInstallFailed, user, target,
                new { path, number = pull.Number, outcome = merged.ToString() });
            logger.LogWarning("Studio CI workflow #{Number} ({Outcome}) by {ActorUserId} in {Repo}", pull.Number, merged, user.UserId, repo.Name);
            return merged == MergeOutcome.Merged
                ? Outcome<StudioResult>.Ok(new StudioResult(
                    $"Added {path} to the agent repo (#{pull.Number}). Set the repo's SKANYXX_URL variable and SKANYXX_CI_TOKEN secret, and enable a runner (setup guide).",
                    pull.Number, null))
                : StudioFailure.Conflict<StudioResult>($"Opened #{pull.Number}, but git did not merge it ({merged}); close it and try again.", "unmergeable");
        }
        catch (StudioRepoException ex)
        {
            return StudioFailure.Conflict<StudioResult>(ex.Message, StudioFailure.RepoReason);
        }
        catch (RepoFileTooLargeException)
        {
            return StudioFailure.Conflict<StudioResult>($"{path} in the agent repo is too large to compare; remove it in git first.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio CI install by {ActorUserId} failed", user.UserId);
            return Outcome<StudioResult>.Unavailable(StudioFailure.Unavailable);
        }
    }
}
