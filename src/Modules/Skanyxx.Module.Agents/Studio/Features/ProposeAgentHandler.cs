using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// Save → PR (D028). The builder's form becomes the agent's two files on a new branch and a pull request; nothing is
/// applied. One open proposal per agent, so a supervisor never merges two competing versions; an edit is a new PR (D2).
/// A name memory holds for someone else is refused here already (D117), and a builder has at most
/// <see cref="StudioOptions.MaxOpenProposalsPerBuilder"/> open at once.
/// </summary>
internal sealed class ProposeAgentHandler(
    IAgentRepo repo, StudioRepoGuard guard, StudioReconciler reconciler, KAgentApiClient kagent, IOptions<StudioOptions> options,
    ILogger<ProposeAgentHandler> logger)
    : IRequestHandler<ProposeAgentCommand, Outcome<StudioResult>>
{
    public async Task<Outcome<StudioResult>> Handle(ProposeAgentCommand command, CancellationToken ct)
    {
        var (user, draft) = (command.User, command.Draft);
        if (!user.CanPropose)
            return Outcome<StudioResult>.Forbidden(StudioFailure.NotBuilder);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured);
        var ns = options.Value.Namespace;
        try
        {
            await guard.RequireAsync(setup: false, ct);
            var models = await kagent.GetModelConfigRefsAsync(ct);
            if (!models.Contains($"{ns}/{draft.ModelConfig}"))
                return StudioFailure.Conflict<StudioResult>($"kagent has no model '{draft.ModelConfig}' in {ns}; pick one the owner configured.");
            await reconciler.EnsureNotForeignAsync(draft.Name, ct);
            if ((await reconciler.PrincipalAsync(draft.Name, ct)).Taken)
                return StudioFailure.Conflict<StudioResult>(StudioFailure.NameTaken(draft.Name), "taken");
            var open = await repo.OpenPullsAsync(ct);
            if (open.FirstOrDefault(p => p.Body.StartsWith($"Agent: {draft.Name}\n", StringComparison.Ordinal)) is { } pending)
                return StudioFailure.Conflict<StudioResult>($"'{draft.Name}' already has an open proposal (#{pending.Number}); a supervisor merges or closes it first.", "pending");
            if (open.Count(p => ProposalTrailer.ProposedBy(p.Body) == user.UserId) >= options.Value.MaxOpenProposalsPerBuilder)
                return StudioFailure.Conflict<StudioResult>(
                    $"You have {options.Value.MaxOpenProposalsPerBuilder} open proposals; close one, or wait for a supervisor, before proposing another.", "too-many");

            var (agentYaml, grantsYaml) = AgentManifest.Render(draft, ns);
            var (agentExists, currentAgent) = await CurrentAsync(StudioNames.AgentPath(draft.Name), ct);
            var (grantsExist, currentGrants) = await CurrentAsync(StudioNames.GrantsPath(draft.Name), ct);
            if (currentAgent == agentYaml && currentGrants == grantsYaml)
                return StudioFailure.Conflict<StudioResult>($"'{draft.Name}' in main is already exactly this.", "unchanged");

            var branch = $"studio/{draft.Name}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var title = agentExists ? $"Change agent {draft.Name}" : $"New agent {draft.Name}";
            var pull = await repo.ProposeAsync(branch,
                [new RepoChange(StudioNames.AgentPath(draft.Name), agentYaml, agentExists), new RepoChange(StudioNames.GrantsPath(draft.Name), grantsYaml, grantsExist)],
                $"{title}\n\nProposed-by: {user.UserId}", user.Name, user.Email, title, ProposalTrailer.Write(draft.Name, user.UserId!, draft.Description), ct);
            logger.LogWarning("Studio proposal #{Number} ({Title}) opened by {ActorUserId} in {Repo}", pull.Number, title, user.UserId, repo.Name);
            return Outcome<StudioResult>.Created(new StudioResult(
                $"Proposed as #{pull.Number}. A supervisor reviews and merges it; until then it is not live.", pull.Number, draft.Name));
        }
        catch (StudioRepoException ex)
        {
            return StudioFailure.Conflict<StudioResult>(ex.Message, StudioFailure.RepoReason);
        }
        catch (StudioApplyException ex)
        {
            return StudioFailure.Conflict<StudioResult>(ex.Message, "taken");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Studio proposal of {Agent} by {ActorUserId} failed", draft.Name, user.UserId);
            return Outcome<StudioResult>.Unavailable(StudioFailure.Unavailable);
        }
    }

    /// <summary>The file in main: whether it is there, and its text when it is one the studio reads.</summary>
    private async Task<(bool Exists, string? Text)> CurrentAsync(string path, CancellationToken ct)
    {
        try
        {
            var text = await repo.ReadAsync(path, options.Value.Git.Branch, ct);
            return (text is not null, text);
        }
        catch (RepoFileTooLargeException)
        {
            return (true, null);
        }
    }
}
