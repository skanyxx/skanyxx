using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Plugins.Features;
using Skanyxx.Module.Agents.Plugins.Sources;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Plugins;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// What a pull request would merge, judged on the files at its head commit. An agent proposal: into main, exactly one
/// agent folder, only its agent.yaml and grants.yaml, both present and valid (the strict shape of
/// <see cref="AgentManifest"/> and the values of <see cref="DraftRules"/>), no name kagent already uses for an agent the
/// studio does not manage and no name memory holds for someone else (D117). A plugin proposal (P8), chosen by its
/// <c>Plugin:</c> trailer and held to it by its files: only that plugin's folder and the agents its plugin.yaml lists
/// (at the head, or in main for what it removes), each valid, recipes only, no secret written out anywhere, no server
/// leaving while an agent outside the plugin uses it, no name another open plugin proposal brings. A catalog plugin must
/// be exactly what the bundled catalog renders; one from git or a zip is the owner's to merge (D222). An agent a plugin
/// owns changes only through its plugin. The head must not move while it is read (m7).
/// </summary>
internal sealed class ProposalInspector(
    IAgentRepo repo, StudioReconciler reconciler, PluginRepoReader plugins, PluginLoader loader, IOptions<StudioOptions> options)
{
    private StudioOptions Options => options.Value;

    public async Task<InspectedProposal> InspectAsync(RepoPull pull, CancellationToken ct)
    {
        if (ProposalTrailer.PluginOf(pull.Body) is { } plugin)
            return await InspectPluginAsync(pull, plugin, ct);

        var problems = new List<string>();
        if (pull.BaseRef != Options.Git.Branch)
            problems.Add($"A proposal merges into {Options.Git.Branch}; this one targets '{pull.BaseRef}'.");
        var changes = await repo.PullFilesAsync(pull.Number, ct);
        var folders = changes.Select(c => c.Path.Split('/')).Select(p => p.Length == 3 && p[0] == StudioNames.AgentsFolder ? p[1] : null).Distinct().ToList();
        string? agent = folders is [{ } only] ? only : null;
        if (agent is null || changes.Count == 0)
            problems.Add($"A proposal changes exactly one folder {StudioNames.AgentsFolder}/<agent>/.");
        if (changes.Any(c => c.Path.Split('/') is not [_, _, StudioNames.AgentFile or StudioNames.GrantsFile]))
            problems.Add($"Only {StudioNames.AgentFile} and {StudioNames.GrantsFile} may change.");
        if (changes.Any(c => !IsAddedOrChanged(c.Status)))
            problems.Add("Files may be added or changed, not deleted or renamed.");

        var files = await ReadFilesAsync(pull, changes.Take(10), problems, ct);

        AgentDraft? draft = null;
        var needsOwner = false;
        if (agent is not null && StudioNames.IsValidName(agent))
        {
            var mainTree = await plugins.ReadAsync(await repo.ListMainAsync(ct), Options.Git.Branch, log: false, ct);
            if (mainTree.OwnerOf(agent) is { } owner)
                problems.Add(StudioFailure.PluginOwned(agent, owner));
            var (head, headProblems) = await reconciler.ReadAgentAsync(agent, pull.HeadSha, mainTree.ServerPlugins, [], ct);
            problems.AddRange(headProblems);
            var (main, _) = await reconciler.ReadAgentAsync(agent, Options.Git.Branch, mainTree.ServerPlugins, [], ct);
            var principal = await reconciler.PrincipalAsync(agent, ct);
            if (principal.Taken)
                problems.Add(StudioFailure.NameTaken(agent));
            // D091 from what is, not only from what main says (m6): main's folder may be invalid while memory still
            // holds the team grant this merge would take away.
            needsOwner = DraftRules.OpensTeamOrDepartment(head?.Grants ?? []) || DraftRules.OpensTeamOrDepartment(main?.Grants ?? [])
                || DraftRules.OpensTeamOrDepartment(principal.Grants);
            await NotForeignAsync(agent, problems, ct);
            if (problems.Count == 0)
                draft = head;
        }
        else if (agent is not null)
            problems.Add($"'{agent}' is not a valid agent name.");

        if (!await HeadStillAsync(pull, ct))
        {
            problems.Add("The proposal changed while it was being read; review it again.");
            draft = null;
        }
        return new InspectedProposal(pull, agent, draft, files, problems, needsOwner);
    }

    private async Task<InspectedProposal> InspectPluginAsync(RepoPull pull, string plugin, CancellationToken ct)
    {
        var problems = new List<string>();
        if (pull.BaseRef != Options.Git.Branch)
            problems.Add($"A proposal merges into {Options.Git.Branch}; this one targets '{pull.BaseRef}'.");
        if (!StudioNames.IsValidPluginName(plugin))
            return new InspectedProposal(pull, null, null, [], [.. problems, $"'{plugin}' is not a valid plugin name."], false) { Plugin = plugin };

        var changes = await repo.PullFilesAsync(pull.Number, ct);
        if (changes.Count == 0)
            problems.Add("A plugin proposal changes its plugin's files.");
        if (changes.Count > StudioOptions.MaxPluginFiles)
            problems.Add($"A plugin proposal changes at most {StudioOptions.MaxPluginFiles} files.");

        var mainPaths = await repo.ListMainAsync(ct);
        var mainTree = await plugins.ReadAsync(mainPaths, Options.Git.Branch, log: false, ct);
        var inMain = mainTree.Plugins.GetValueOrDefault(plugin);
        var manifestPath = StudioNames.PluginManifestPath(plugin);
        var removal = changes.Any(c => c.Path == manifestPath && IsDeleted(c.Status));
        InstalledPlugin? head = null;
        if (removal)
        {
            if (inMain is null)
                problems.Add($"main has no valid {manifestPath} to remove.");
        }
        else
        {
            var (read, problem) = await plugins.ReadPluginAsync(plugin, pull.HeadSha, ct);
            head = read;
            if (problem is not null)
                problems.Add(problem);
        }

        // Every path is the plugin's: added or changed only as the head's plugin.yaml lists it, removed only as main's
        // lists it (and the head's no longer does), and what the plugin no longer has in main is removed with it.
        string[] headAgents = [.. head?.Agents ?? []], mainAgents = [.. inMain?.Agents ?? []];
        string[] headServers = [.. head?.Servers ?? []], mainServers = [.. inMain?.Servers ?? []];
        foreach (var change in changes)
        {
            var removed = IsDeleted(change.Status);
            if (!removed && !IsAddedOrChanged(change.Status))
            {
                problems.Add($"{change.Path}: a plugin proposal adds, changes or removes files, never renames them.");
                continue;
            }
            var owned = change.Path == manifestPath || (change.Path.Split('/') switch
            {
                [StudioNames.PluginsFolder, var p, StudioNames.McpFolder, _] when p == plugin && StudioNames.ServerOf(change.Path) is var (_, server) =>
                    removed ? mainServers.Contains(server) && !headServers.Contains(server) && mainPaths.Contains(change.Path) : headServers.Contains(server),
                [StudioNames.AgentsFolder, var agent, StudioNames.AgentFile or StudioNames.GrantsFile] =>
                    removed ? mainAgents.Contains(agent) && !headAgents.Contains(agent) && mainPaths.Contains(change.Path) : headAgents.Contains(agent),
                _ => false
            });
            if (!owned)
                problems.Add($"{change.Path} is not a file this plugin proposal may {(removed ? "remove" : "add or change")}: "
                    + $"only its plugin folder and the agents its {StudioNames.PluginFile} lists.");
        }
        var deleted = changes.Where(c => IsDeleted(c.Status)).Select(c => c.Path).ToHashSet();
        var leftBehind = mainPaths.Where(path => !deleted.Contains(path)
            && ((removal && path.StartsWith(StudioNames.PluginFolder(plugin) + "/", StringComparison.Ordinal))
                || mainAgents.Except(headAgents).Any(a => path.StartsWith(StudioNames.Folder(a) + "/", StringComparison.Ordinal))
                || mainServers.Except(headServers).Any(s => path == StudioNames.ServerPath(plugin, s)))).ToList();
        if (leftBehind.Count > 0)
            problems.Add($"What the plugin no longer has must be removed with it: {string.Join(", ", leftBehind.Take(10))}.");

        var files = await ReadFilesAsync(pull, changes.Where(c => !IsDeleted(c.Status)).Take(StudioOptions.MaxPluginFiles), problems, ct);
        files.AddRange(changes.Where(c => IsDeleted(c.Status)).Select(c => new ProposalFile(c.Path, c.Status, null)));
        foreach (var file in files.Where(f => f.Content is not null))
            if (SecretScan.Find(file.Content!) is { } secret)
                problems.Add($"{file.Path} seems to hold {secret}; header values go to the server's Secret, never to git.");

        // A server that leaves must not leave an agent outside the plugin bound to nothing (D219).
        var mainFolders = StudioReconciler.MainFolders(mainPaths);
        var leaving = removal ? mainServers : mainServers.Except(headServers).ToArray();
        if (leaving.Length > 0
            && PluginUsers.Problem(leaving, await reconciler.ServerUsersAsync(mainFolders.Except(mainAgents), ct)) is { } used)
            problems.Add(used);

        var drafts = new List<AgentDraft>();
        var servers = new Dictionary<string, PluginServerEntry>();
        var notes = new List<string>();
        if (head is not null)
        {
            var kagentServers = await reconciler.ToolServersAsync(ct);
            var records = await reconciler.PluginRecordsAsync(ct);
            foreach (var server in head.Servers)
            {
                if (await plugins.ReadServerAsync(head, server, pull.HeadSha, servers, ct) is { } problem)
                    problems.Add(problem);
                if (mainTree.ServerOwnerOf(server) is { } owner && owner != plugin)
                    problems.Add($"The server '{server}' belongs to the plugin '{owner}'.");
                var record = records.GetValueOrDefault(server);
                if (kagentServers.Contains($"{Options.Namespace}/{server}") && !(record is { Claimed: true } && record.Plugin == plugin))
                    problems.Add($"kagent already has a server '{server}' that Skanyxx did not create for this plugin; choose another name.");
                if (servers.GetValueOrDefault(server) is { } entry && record is { ValueNames.Count: > 0 } && record.Plugin == plugin
                    && record.ValueAddress != PluginServerUrl.Address(entry.Server.Url))
                    notes.Add($"The server '{server}' moves to {PluginServerUrl.Address(entry.Server.Url)}; the header values Skanyxx holds were entered for "
                        + $"{record.ValueAddress ?? "another address"} and are not sent there: it waits for the owner to enter them again.");
            }

            var allowed = new Dictionary<string, string>(mainTree.ServerPlugins);
            foreach (var server in head.Servers)
                allowed[server] = plugin;
            foreach (var agent in head.Agents)
            {
                var (draft, agentProblems) = await reconciler.ReadAgentAsync(agent, pull.HeadSha, allowed, head.Servers, ct);
                problems.AddRange(agentProblems);
                if (draft is not null)
                {
                    problems.AddRange(PluginRules.CheckRecipe(draft, head.Servers));
                    drafts.Add(draft);
                }
                var owner = mainTree.OwnerOf(agent);
                if (owner is not null && owner != plugin)
                    problems.Add($"The agent '{agent}' belongs to the plugin '{owner}'.");
                else if (owner is null && mainFolders.Contains(agent))
                    problems.Add($"The agent '{agent}' is a studio agent no plugin owns; a plugin cannot take it over.");
                if ((await reconciler.PrincipalAsync(agent, ct)).Taken)
                    problems.Add(StudioFailure.NameTaken(agent));
                await NotForeignAsync(agent, problems, ct);
            }
            var open = (await repo.OpenPullsAsync(ct)).Where(p => p.Number != pull.Number);
            if (await PluginPending.FindAsync(repo, open, plugin, head.Agents, head.Servers, ct) is { } pending)
                problems.Add(pending);
            if (head.Source.Kind == CatalogPluginSource.Kind)
                problems.AddRange(await CatalogMismatchAsync(head, changes, mainPaths, files, ct));
        }

        if (!await HeadStillAsync(pull, ct))
            problems.Add("The proposal changed while it was being read; review it again.");
        return new InspectedProposal(pull, null, null, files, problems, NeedsOwner: head?.Source.Kind is "git" or "zip")
        {
            Plugin = plugin,
            Removal = removal,
            Notes = notes,
            Drafts = problems.Count == 0 ? drafts : [],
            Servers = problems.Count == 0 ? [.. servers.Values.Select(s => s.Server)] : []
        };
    }

    /// <summary>
    /// A catalog plugin ships with Skanyxx, which is why a builder may bring it in and a supervisor merge it: what main
    /// would hold after the merge must be exactly what this build's catalog renders (D222). A branch edited in git, or a
    /// catalog that changed since the install, is refused (install it again).
    /// </summary>
    private async Task<IEnumerable<string>> CatalogMismatchAsync(InstalledPlugin head, IReadOnlyList<RepoFileChange> changes, IReadOnlyList<string> mainPaths,
        List<ProposalFile> files, CancellationToken ct)
    {
        Dictionary<string, string> expected;
        try
        {
            var read = await loader.LoadAsync(new PluginSourceRequest { Source = CatalogPluginSource.Kind, Name = head.Source.Location }, null, mayFollowGit: false, ct);
            if (read.Plugin is not { } draft || draft.Name != head.Name)
                return [$"The catalog has no plugin '{head.Source.Location}' named '{head.Name}'; install it again from the catalog."];
            expected = PluginRender.Files(draft, Options.Namespace);
        }
        catch (PluginSourceException ex)
        {
            return [$"The catalog plugin '{head.Source.Location}' cannot be read ({ex.Message}); install it again from the catalog."];
        }
        var deleted = changes.Where(c => IsDeleted(c.Status)).Select(c => c.Path).ToHashSet();
        var mismatched = new List<string>();
        foreach (var path in files.Where(f => f.Content is not null).Select(f => f.Path).Concat(expected.Keys).Distinct())
        {
            var after = files.FirstOrDefault(f => f.Path == path && f.Content is not null)?.Content
                ?? (mainPaths.Contains(path) && !deleted.Contains(path) ? await ReadMainAsync(path, ct) : null);
            if (after != expected.GetValueOrDefault(path))
                mismatched.Add(path);
        }
        return mismatched.Count == 0
            ? []
            : [$"This catalog plugin is not what Skanyxx's catalog renders ({string.Join(", ", mismatched.Take(10))}); a catalog proposal is merged only as installed. Install it again."];
    }

    private async Task<string?> ReadMainAsync(string path, CancellationToken ct)
    {
        try
        {
            return await repo.ReadAsync(path, Options.Git.Branch, ct);
        }
        catch (RepoFileTooLargeException)
        {
            return null;
        }
    }

    private async Task<List<ProposalFile>> ReadFilesAsync(RepoPull pull, IEnumerable<RepoFileChange> changes, List<string> problems, CancellationToken ct)
    {
        var files = new List<ProposalFile>();
        foreach (var change in changes)
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
        return files;
    }

    private async Task NotForeignAsync(string agent, List<string> problems, CancellationToken ct)
    {
        try
        {
            await reconciler.EnsureNotForeignAsync(agent, ct);
        }
        catch (StudioApplyException ex)
        {
            problems.Add(ex.Message);
        }
    }

    /// <summary>The file list is the current head's, the contents are HeadSha's: they are one commit only if the head did not move.</summary>
    private async Task<bool> HeadStillAsync(RepoPull pull, CancellationToken ct) =>
        await repo.PullAsync(pull.Number, ct) is { } now && now.HeadSha == pull.HeadSha;

    private static bool IsAddedOrChanged(string status) => status is "added" or "modified" or "changed";

    /// <summary>Gitea says <c>deleted</c>, GitHub <c>removed</c>.</summary>
    private static bool IsDeleted(string status) => status is "deleted" or "removed";
}
