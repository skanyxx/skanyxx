using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// What a pull request would merge, judged on the files at its head commit: into main, exactly one agent folder, only
/// its agent.yaml and grants.yaml, both present and valid (the strict shape of <see cref="AgentManifest"/> and the
/// values of <see cref="DraftRules"/>), no name kagent already uses for an agent the studio does not manage and no name
/// memory holds for someone else (D117). The head must not move while it is read (m7).
/// </summary>
internal sealed class ProposalInspector(IAgentRepo repo, StudioReconciler reconciler, IOptions<StudioOptions> options)
{
    public async Task<InspectedProposal> InspectAsync(RepoPull pull, CancellationToken ct)
    {
        var problems = new List<string>();
        if (pull.BaseRef != options.Value.Git.Branch)
            problems.Add($"A proposal merges into {options.Value.Git.Branch}; this one targets '{pull.BaseRef}'.");
        var changes = await repo.PullFilesAsync(pull.Number, ct);
        var folders = changes.Select(c => c.Path.Split('/')).Select(p => p.Length == 3 && p[0] == StudioNames.AgentsFolder ? p[1] : null).Distinct().ToList();
        string? agent = folders is [{ } only] ? only : null;
        if (agent is null || changes.Count == 0)
            problems.Add($"A proposal changes exactly one folder {StudioNames.AgentsFolder}/<agent>/.");
        if (changes.Any(c => c.Path.Split('/') is not [_, _, StudioNames.AgentFile or StudioNames.GrantsFile]))
            problems.Add($"Only {StudioNames.AgentFile} and {StudioNames.GrantsFile} may change.");
        if (changes.Any(c => c.Status is not ("added" or "modified" or "changed")))
            problems.Add("Files may be added or changed, not deleted or renamed.");

        var files = new List<ProposalFile>();
        foreach (var change in changes.Take(10))
        {
            string? content;
            try
            {
                content = await repo.ReadAsync(change.Path, pull.HeadSha, ct);
            }
            catch (RepoFileTooLargeException)
            {
                content = null;
                problems.Add($"{change.Path} is larger than {IAgentRepo.MaxFileBytes / 1024} KB.");
            }
            files.Add(new ProposalFile(change.Path, change.Status, content));
        }

        AgentDraft? draft = null;
        var needsOwner = false;
        if (agent is not null && StudioNames.IsValidName(agent))
        {
            var (head, headProblems) = await reconciler.ReadAgentAsync(agent, pull.HeadSha, ct);
            problems.AddRange(headProblems);
            var (main, _) = await reconciler.ReadAgentAsync(agent, options.Value.Git.Branch, ct);
            var principal = await reconciler.PrincipalAsync(agent, ct);
            if (principal.Taken)
                problems.Add(StudioFailure.NameTaken(agent));
            // D091 from what is, not only from what main says (m6): main's folder may be invalid while memory still
            // holds the team grant this merge would take away.
            needsOwner = DraftRules.OpensTeamOrDepartment(head?.Grants ?? []) || DraftRules.OpensTeamOrDepartment(main?.Grants ?? [])
                || DraftRules.OpensTeamOrDepartment(principal.Grants);
            try
            {
                await reconciler.EnsureNotForeignAsync(agent, ct);
            }
            catch (StudioApplyException ex)
            {
                problems.Add(ex.Message);
            }
            if (problems.Count == 0)
                draft = head;
        }
        else if (agent is not null)
            problems.Add($"'{agent}' is not a valid agent name.");

        // The file list is the current head's, the contents are HeadSha's: they are one commit only if the head did not move.
        if (await repo.PullAsync(pull.Number, ct) is not { } now || now.HeadSha != pull.HeadSha)
        {
            problems.Add("The proposal changed while it was being read; review it again.");
            draft = null;
        }
        return new InspectedProposal(pull, agent, draft, files, problems, needsOwner);
    }
}
