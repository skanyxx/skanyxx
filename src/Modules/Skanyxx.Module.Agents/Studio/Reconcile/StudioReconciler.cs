using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Plugins;

namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>
/// Our reconciler (D045): makes kagent and memory match main of the agent repo, through kagent's HTTP API — never
/// kubectl. For each agent folder in main: its grants into memory, its own memory server with a studio-issued secret
/// (only when it has a grant), and the Agent CR labelled merged (D100). An agent the studio manages that left main is
/// removed, behind the removal brake (D119); a preview whose pull request is no longer open is removed. Plugins (P6, P8):
/// their remote MCP servers first, each with the header values memory holds as a fingerprinted Secret (D118's pattern);
/// one that lacks a value is not created, and an agent using a plugin server kagent does not run is held. A plugin server
/// that left main goes after the agents, under the same brake, and never while an agent in main still names it (D219). A
/// server Skanyxx did not create is never touched; header values nothing can deploy any more are deleted (D220).
/// Idempotent: an agent or server that already matches is not written. One run at a time across every replica (D120):
/// the periodic pass skips when another holds the lock, everything else waits for it a bounded time.
/// </summary>
internal sealed class StudioReconciler(
    KAgentApiClient kagent, IAgentRepo repo, PluginRepoReader plugins, StudioRepoGuard guard, IStudioReconcileLock locks, IServiceScopeFactory scopes,
    IOptions<StudioOptions> options, ILogger<StudioReconciler> logger)
{
    public const string Actor = "studio:reconcile";

    /// <summary>An install stores its values just before its proposal opens: a record younger than this is never purged.</summary>
    public static readonly TimeSpan PurgeAfter = TimeSpan.FromHours(1);

    private readonly StudioOptions _options = options.Value;

    private string Ns => _options.Namespace;

    /// <summary>The periodic pass. Agents and servers that fail are logged and retried next time; the rest still apply.</summary>
    /// <returns>
    /// How many agents and plugin servers failed to apply or be removed, or wait for the owner's confirmation; 0 when
    /// another replica is reconciling.
    /// </returns>
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        await using var held = await locks.TryAcquireAsync(TimeSpan.Zero, ct);
        if (held is null)
        {
            logger.LogInformation("Studio reconcile skipped: another replica holds the reconcile lock");
            return 0;
        }
        var result = await PassAsync(allowMassRemoval: false, ct);
        return result.Failures + result.Braked;
    }

    /// <summary>The owner's confirmed pass (D119): the removal brake does not apply.</summary>
    public async Task<int> ConfirmedPassAsync(CancellationToken ct)
    {
        await using var held = await HoldAsync(ct);
        return (await PassAsync(allowMassRemoval: true, ct)).Failures;
    }

    /// <summary>After a plugin proposal's merge: a whole pass (its servers, then its agents, then what it removed, braked).</summary>
    public async Task<ReconcileResult> ApplyAllAsync(CancellationToken ct)
    {
        await using var held = await HoldAsync(ct);
        return await PassAsync(allowMassRemoval: false, ct);
    }

    private async Task<ReconcileResult> PassAsync(bool allowMassRemoval, CancellationToken ct)
    {
        await guard.RequireAsync(setup: true, ct);
        var paths = await repo.ListMainAsync(ct);
        var folders = MainFolders(paths);
        var tree = await plugins.ReadAsync(paths, _options.Git.Branch, log: true, ct);
        var main = await ReadMainAsync(folders, tree, log: true, ct);
        var desired = main.Valid;
        var servers = await ToolServersAsync(ct);
        var records = await PluginRecordsAsync(ct);
        var failures = 0;
        var braked = 0;
        var keptInUse = new List<string>();

        // Servers before the agents that use them (P8).
        var live = new HashSet<string>();
        foreach (var entry in tree.Servers.Values)
        {
            try
            {
                if (await ApplyServerAsync(entry, servers, records, ct))
                    live.Add(entry.Server.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning(ex, "Studio could not apply the plugin server {Server}; retrying on the next pass", entry.Server.Name);
            }
        }

        foreach (var draft in desired.Values)
        {
            if (draft.McpTools.Select(t => t.Server).FirstOrDefault(s => IsPluginServer(s, tree.ServerPlugins) && !live.Contains(s)) is { } waiting)
            {
                // Held, not removed: a running version stays as it is until its server is there.
                logger.LogWarning("Studio holds the agent {Agent}: its plugin server {Server} is not running in kagent (it needs a secret, is invalid or failed)",
                    draft.Name, waiting);
                continue;
            }
            try
            {
                await ApplyCoreAsync(draft, Actor, servers, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning(ex, "Studio could not apply the agent {Agent}; retrying on the next pass", draft.Name);
            }
        }

        var pulls = await repo.OpenPullsAsync(ct);
        var open = pulls.Select(p => p.Number).ToHashSet();
        var managed = (await kagent.GetAgentsAsync(ct))
            .Where(a => a.Namespace == Ns && a.Labels.GetValueOrDefault(StudioNames.ManagedByLabel) == StudioNames.ManagedBy).ToList();
        var merged = managed.Where(a => !a.Labels.ContainsKey(StudioNames.PreviewLabel) && !a.Labels.ContainsKey(StudioNames.RoleLabel)).ToList();
        var gone = merged.Where(a => !folders.Contains(a.Name)).Select(a => a.Name).ToList();
        // Only servers the studio created, never an allow-listed one; one whose file is still in main (even invalid) stays.
        var goneServers = records.Values.Where(r => r.Claimed && !tree.ServerNames.Contains(r.Server) && !_options.McpServers.Contains(r.Server))
            .Select(r => r.Server).ToList();
        // An agent in main that still names the server would be left bound to nothing (D219): the server stays until a
        // person changes those agents. Not a failure: no retry fixes it.
        // A steady state that waits for a person, not a new failure: one Warning per pass, not an Error per server.
        keptInUse.AddRange(goneServers.Where(main.ServerUsers.ContainsKey));
        goneServers.RemoveAll(keptInUse.Contains);
        if (keptInUse.Count > 0)
            logger.LogWarning("Studio keeps the plugin servers that left main while agents in main still use them: {Servers}",
                string.Join("; ", keptInUse.Select(s => $"{s} (used by {string.Join(", ", main.ServerUsers[s])})")));
        if (gone.Count + goneServers.Count > 0 && !allowMassRemoval
            && Brake(paths, gone.Count, gone.Count + goneServers.Count, merged.Count + records.Values.Count(r => r.Claimed)) is { } reason)
        {
            logger.LogError("Studio removes no agent this pass (nor plugin server): {Reason} Would remove: {Agents}. {Hint}", reason,
                string.Join(", ", gone.Concat(goneServers)), StudioRepoGuard.ConfirmHint);
            braked += gone.Count + goneServers.Count;
            gone = [];
            goneServers = [];
        }
        // A preview whose proposal is not open, whether kagent still runs it or only its memory server is left (a
        // preview that failed half way, m8). kagent lists tool servers without labels, so a lone server counts as the
        // studio's only with a numbered preview name AND memory's record that the studio claimed that principal (round 2
        // m5); anything else under the prefix is someone's own and stays.
        gone.AddRange(managed.Where(a => a.Labels.TryGetValue(StudioNames.PreviewLabel, out var pr) && !(int.TryParse(pr, out var n) && open.Contains(n)))
            .Select(a => a.Name));
        foreach (var orphan in servers.Where(s => s.StartsWith($"{Ns}/{StudioNames.MemoryServerPrefix}preview-", StringComparison.Ordinal))
            .Select(s => s[(Ns.Length + 1 + StudioNames.MemoryServerPrefix.Length)..])
            .Where(a => StudioNames.PreviewNumber(a) is { } n && !open.Contains(n)))
            if ((await PrincipalAsync(orphan, ct)).Claimed)
                gone.Add(orphan);

        var agentLeft = false;
        foreach (var agent in gone.Distinct())
        {
            logger.LogWarning("Studio agent {Agent} is no longer in {Repo} (or its proposal closed); removing it from kagent", agent, repo.Name);
            try
            {
                await RemoveAsync(agent, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures++;
                agentLeft = true;
                logger.LogWarning(ex, "Studio could not remove {Agent}; retrying on the next pass", agent);
            }
        }

        // Agents first, then the servers they used (P8): none goes while an agent that may use it is still there.
        if (agentLeft && goneServers.Count > 0)
        {
            logger.LogWarning("Studio keeps the plugin servers {Servers} until the agents leaving with them are gone", string.Join(", ", goneServers));
            failures += goneServers.Count;
            goneServers = [];
        }
        foreach (var server in goneServers)
        {
            try
            {
                await RemoveServerAsync(server, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning(ex, "Studio could not remove the plugin server {Server}; retrying on the next pass", server);
            }
        }

        try
        {
            await PurgeAsync(records.Values, pulls, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Studio could not delete stale plugin header values; retrying on the next pass");
        }
        await EnsureFactoryCoreAsync(ct);
        return new ReconcileResult(failures, braked, keptInUse);
    }

    /// <summary>
    /// Header values stored for a server that neither main nor an open proposal assigns to their plugin (a proposal closed
    /// in git, a server dropped before it ever ran, a row another plugin left behind) are deleted: nothing would send them,
    /// and a later install must not inherit them or be blocked by them (D220). A record younger than
    /// <see cref="PurgeAfter"/> is spared, since an install stores values just before its proposal opens. Main is listed
    /// after <paramref name="open"/>, so a proposal merged between the two reads is in main by then, never in neither.
    /// </summary>
    private async Task PurgeAsync(IEnumerable<PluginServerRecord> records, IReadOnlyList<RepoPull> open, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - PurgeAfter;
        var old = records.Where(r => !r.Claimed && r.UpdatedAt < cutoff).ToList();
        if (old.Count == 0)
            return;
        var kept = (await MainPluginServersAsync(ct)).Select(s => (Server: s.Key, Plugin: s.Value)).ToHashSet();
        foreach (var pull in open.Where(p => ProposalTrailer.PluginOf(p.Body) is not null))
            kept.UnionWith(PluginRepoReader.ServerPlugins((await repo.PullFilesAsync(pull.Number, ct)).Select(f => f.Path)).Select(s => (Server: s.Key, Plugin: s.Value)));
        foreach (var record in old.Where(r => !kept.Contains((r.Server, r.Plugin))))
            await RemoveValuesAsync(record.Server, record.Plugin, cutoff, ct);
    }

    /// <summary>
    /// A closed plugin proposal: the values its install stored for servers main does not have are deleted at once (D220).
    /// </summary>
    /// <param name="closedAt">Taken before the close: a record a reinstall stored since is kept.</param>
    public async Task PurgeClosedAsync(string plugin, IReadOnlySet<string> servers, DateTime closedAt, CancellationToken ct)
    {
        var main = await MainPluginServersAsync(ct);
        foreach (var record in (await PluginRecordsAsync(ct)).Values.Where(r => servers.Contains(r.Server) && r.Plugin == plugin && !r.Claimed && !main.ContainsKey(r.Server)))
            await RemoveValuesAsync(record.Server, plugin, closedAt, ct);
    }

    /// <param name="updatedBefore">The record is kept if it changed since (a reinstall stored values meanwhile).</param>
    private async Task RemoveValuesAsync(string server, string plugin, DateTime updatedBefore, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        if (Require(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
                new RemovePluginServerCommand(Actor, server, UnclaimedOnly: true, plugin, updatedBefore), ct)))
            logger.LogWarning("Studio deleted the stored header values of the plugin server {Server}: no proposal and nothing in main can deploy it", server);
    }

    /// <summary>
    /// Why this pass must not remove <paramref name="gone"/> (of which <paramref name="goneAgents"/> agents) of the
    /// <paramref name="total"/> merged agents and plugin servers, or null when it may.
    /// </summary>
    private string? Brake(IReadOnlyList<string> paths, int goneAgents, int gone, int total)
    {
        if (goneAgents > 0 && !paths.Any(p => p.StartsWith(StudioNames.AgentsFolder + "/", StringComparison.Ordinal)))
            return $"main of {repo.Name} has no {StudioNames.AgentsFolder}/ folder while kagent runs studio agents.";
        if (gone > _options.MaxRemovalsPerPass)
            return $"{gone} agents and plugin servers would leave at once (Studio:MaxRemovalsPerPass is {_options.MaxRemovalsPerPass}).";
        if (gone > 1 && gone * 2 > total)
            return $"{gone} of {total} studio agents and plugin servers would leave at once.";
        return null;
    }

    /// <summary>After a merge: the merged agent now (as <paramref name="actor"/>, for the audit), and its preview gone.</summary>
    /// <returns>False when the owner suspended the agent (D121): merged, but kept out of kagent.</returns>
    public async Task<bool> ApplyMergedAsync(AgentDraft draft, int number, string actor, CancellationToken ct)
    {
        await using var held = await HoldAsync(ct);
        var servers = await ToolServersAsync(ct);
        var pluginServers = await MainPluginServersAsync(ct);
        if (draft.McpTools.Any(t => IsPluginServer(t.Server, pluginServers)))
        {
            // Held like the pass holds it: an agent never binds a plugin server the studio is not running.
            var records = await PluginRecordsAsync(ct);
            if (draft.McpTools.Select(t => t.Server).FirstOrDefault(s => IsPluginServer(s, pluginServers)
                    && !(servers.Contains($"{Ns}/{s}") && records.GetValueOrDefault(s) is { Claimed: true })) is { } waiting)
                throw new StudioApplyException($"its plugin server {waiting} is not running in kagent yet (it may need a header value from the owner); the agent follows it");
        }
        await RemoveAsync(StudioNames.PreviewAgent(number, draft.Name), ct);
        servers.Remove($"{Ns}/{StudioNames.MemoryServer(StudioNames.PreviewAgent(number, draft.Name))}");
        return await ApplyCoreAsync(draft, actor, servers, ct);
    }

    /// <summary>
    /// Deploys (or refreshes) the proposal's preview agent (D030). A preview that fails half way is taken down again, so
    /// no grant, secret or memory server outlives it.
    /// </summary>
    public async Task<PreviewAgent> PreviewAsync(AgentDraft draft, int number, string actor, CancellationToken ct)
    {
        await using var held = await HoldAsync(ct);
        if ((await PrincipalAsync(draft.Name, ct)).Suspended)
            throw new StudioApplyException($"The owner suspended {draft.Name}; it has no preview until the owner resumes it.");
        var preview = KAgentObjects.Preview(draft, number);
        await EnsureNotForeignAsync(preview.Name, ct);
        var principal = await PrincipalAsync(preview.Name, ct);
        try
        {
            await ApplyMemoryAsync(preview.Name, preview.Grants, actor, principal, await ToolServersAsync(ct), ct);
            await PutAgentAsync(KAgentObjects.WithLabels(AgentManifest.Agent(preview, Ns),
                (StudioNames.ManagedByLabel, StudioNames.ManagedBy), (StudioNames.PreviewLabel, number.ToString())), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            try
            {
                await RemoveAsync(preview.Name, CancellationToken.None);
            }
            catch (Exception cleanup) when (cleanup is not OperationCanceledException)
            {
                logger.LogWarning(cleanup, "Studio could not take down the failed preview {Agent}; the next pass removes it", preview.Name);
            }
            throw;
        }
        var agent = (await kagent.GetAgentsAsync(ct)).FirstOrDefault(a => a.Namespace == Ns && a.Name == preview.Name);
        return new PreviewAgent(Ns, preview.Name, agent?.Ready == true);
    }

    public async Task RemovePreviewAsync(int number, string agent, CancellationToken ct)
    {
        await using var held = await HoldAsync(ct);
        await RemoveAsync(StudioNames.PreviewAgent(number, agent), ct);
    }

    /// <summary>
    /// The owner suspended <paramref name="agent"/> (D121): out of kagent now, previews of its open proposals with it;
    /// memory already revoked it.
    /// </summary>
    public async Task TakeDownAsync(string agent, CancellationToken ct)
    {
        await using var held = await HoldAsync(ct);
        await RemoveFromKagentAsync(agent, ct);
        foreach (var preview in (await kagent.GetAgentsAsync(ct)).Where(a => a.Namespace == Ns
                     && a.Labels.TryGetValue(StudioNames.PreviewLabel, out var pr) && int.TryParse(pr, out var n) && a.Name == StudioNames.PreviewAgent(n, agent)))
            await RemoveAsync(preview.Name, ct);
    }

    /// <summary>The factory agent (D5), under the reconcile lock (m9): not merged (never in Chat), no tools, the owner's model.</summary>
    public async Task EnsureFactoryAsync(CancellationToken ct)
    {
        await using var held = await HoldAsync(ct);
        await EnsureFactoryCoreAsync(ct);
    }

    private Task EnsureFactoryCoreAsync(CancellationToken ct) => PutAgentAsync(KAgentObjects.WithLabels(new JsonObject
    {
        ["apiVersion"] = "kagent.dev/v1alpha2",
        ["kind"] = "Agent",
        ["metadata"] = new JsonObject { ["name"] = StudioNames.FactoryAgent, ["namespace"] = Ns },
        ["spec"] = new JsonObject
        {
            ["type"] = "Declarative",
            ["description"] = "Drafts studio agents for builders. Not a chat partner: Skanyxx calls it from the studio only.",
            ["declarative"] = new JsonObject { ["modelConfig"] = _options.FactoryModelConfig, ["systemMessage"] = FactoryPrompt.Text }
        }
    }, (StudioNames.ManagedByLabel, StudioNames.ManagedBy), (StudioNames.RoleLabel, "factory")), ct);

    /// <summary>The agent folders in main: every folder under <c>agents/</c>, valid or not (an invalid one is skipped, never removed).</summary>
    public static HashSet<string> MainFolders(IEnumerable<string> paths) =>
        [.. paths.Select(p => p.Split('/')).Where(p => p.Length >= 3 && p[0] == StudioNames.AgentsFolder).Select(p => p[1])];

    /// <summary>The valid agents in main (see <see cref="ReadMainAsync(IEnumerable{string}, RepoPlugins, bool, CancellationToken)"/>), for a page.</summary>
    public async Task<Dictionary<string, AgentDraft>> ReadMainAsync(CancellationToken ct)
    {
        var paths = await repo.ListMainAsync(ct);
        return (await ReadMainAsync(MainFolders(paths), await plugins.ReadAsync(paths, _options.Git.Branch, log: false, ct), log: false, ct)).Valid;
    }

    /// <summary>
    /// The plugin servers declared in main, each with its plugin: with <see cref="StudioOptions.McpServers"/>, what an agent
    /// may use (P7).
    /// </summary>
    public async Task<Dictionary<string, string>> MainPluginServersAsync(CancellationToken ct) => PluginRepoReader.ServerPlugins(await repo.ListMainAsync(ct));

    /// <summary>
    /// Each MCP server the <c>agent.yaml</c> of <paramref name="folders"/> in main names → those agents, valid or not: a
    /// request's check that a leaving server has no user (D219). One read per folder and nothing logged; the pass logs
    /// what is invalid.
    /// </summary>
    public async Task<Dictionary<string, SortedSet<string>>> ServerUsersAsync(IEnumerable<string> folders, CancellationToken ct)
    {
        var users = new Dictionary<string, SortedSet<string>>();
        foreach (var name in folders.Where(StudioNames.IsValidName))
        {
            string? agentYaml;
            try
            {
                agentYaml = await repo.ReadAsync(StudioNames.AgentPath(name), _options.Git.Branch, ct);
            }
            catch (RepoFileTooLargeException)
            {
                continue;
            }
            foreach (var server in agentYaml is null ? [] : AgentManifest.McpServers(name, agentYaml))
                (users.TryGetValue(server, out var agents) ? agents : users[server] = []).Add(name);
        }
        return users;
    }

    /// <summary>
    /// The agents among <paramref name="folders"/> of main: the valid ones (an invalid folder is logged and never applied,
    /// and the agent it describes is left as it runs, not removed), and which MCP servers each folder names, valid or not.
    /// A plugin's own agents (<paramref name="tree"/>) may use its servers with no tool names, and are held to the recipe
    /// rules (D005) here too, so a hand edit of main cannot give one memory; an agent two plugins list is never applied.
    /// </summary>
    /// <param name="log">Log what is invalid at Error (the pass); a page view does not.</param>
    public async Task<MainAgents> ReadMainAsync(IEnumerable<string> folders, RepoPlugins tree, bool log, CancellationToken ct)
    {
        var main = new MainAgents([], []);
        foreach (var name in folders)
        {
            var (shape, problem, agentYaml) = await ReadShapeAsync(name, _options.Git.Branch, ct);
            List<string> problems = shape is null ? [problem!] : [.. DraftRules.Check(shape, _options, tree.ServerPlugins, OwnServers(tree, name))];
            if (shape is not null && tree.OwnerOf(name) is not null)
                problems.AddRange(PluginRules.CheckRecipe(shape, OwnServers(tree, name)));
            if (tree.SharedAgents.Contains(name))
                problems.Add($"More than one plugin lists the agent '{name}'; it belongs to none.");
            // An invalid folder still runs as it was (it is never removed), so it counts as a user like a request's check.
            var named = shape?.McpTools.Select(t => t.Server) ?? (agentYaml is null ? [] : AgentManifest.McpServers(name, agentYaml));
            foreach (var server in named)
                (main.ServerUsers.TryGetValue(server, out var users) ? users : main.ServerUsers[server] = []).Add(name);
            if (problems.Count == 0)
                main.Valid[name] = shape!;
            else if (log)
                logger.LogError("Studio skips {Folder} in main of {Repo}: {Problems}", StudioNames.Folder(name), repo.Name, string.Join(" ", problems));
        }
        return main;
    }

    /// <summary>
    /// The servers of the plugin that owns <paramref name="agent"/> in <paramref name="tree"/> whose file is in that
    /// plugin's folder (a manifest cannot make another plugin's server its own); none for any other agent.
    /// </summary>
    public static IReadOnlyList<string> OwnServers(RepoPlugins tree, string agent) =>
        tree.OwnerOf(agent) is { } owner ? [.. tree.Plugins[owner].Servers.Where(s => tree.ServerPlugins.GetValueOrDefault(s) == owner)] : [];

    /// <summary>One agent's folder at a branch or commit, as a form, with every reason it is not a valid studio agent.</summary>
    /// <param name="pluginServers">The plugin servers it may use besides the allow-list (P7).</param>
    /// <param name="ownServers">Its own plugin's servers, for a plugin's agent; none for any other.</param>
    public async Task<(AgentDraft? Draft, IReadOnlyList<string> Problems)> ReadAgentAsync(
        string name, string reference, IReadOnlyDictionary<string, string> pluginServers, IReadOnlyCollection<string> ownServers, CancellationToken ct)
    {
        var (shape, problem, _) = await ReadShapeAsync(name, reference, ct);
        if (shape is null)
            return (null, [problem!]);
        var problems = DraftRules.Check(shape, _options, pluginServers, ownServers);
        return problems.Count == 0 ? (shape, []) : (null, problems);
    }

    /// <summary>
    /// The agent the folder's files describe, held to their strict shape only (not yet to <see cref="DraftRules"/>), and
    /// its <c>agent.yaml</c> as read (null when missing or too large).
    /// </summary>
    private async Task<(AgentDraft? Shape, string? Problem, string? AgentYaml)> ReadShapeAsync(string name, string reference, CancellationToken ct)
    {
        if (!StudioNames.IsValidName(name))
            return (null, $"'{name}' is not a valid agent name.", null);
        string? agentYaml = null, grantsYaml;
        try
        {
            agentYaml = await repo.ReadAsync(StudioNames.AgentPath(name), reference, ct);
            grantsYaml = await repo.ReadAsync(StudioNames.GrantsPath(name), reference, ct);
        }
        catch (RepoFileTooLargeException)
        {
            return (null, $"Agent files are at most {IAgentRepo.MaxFileBytes / 1024} KB.", agentYaml);
        }
        if (agentYaml is null || grantsYaml is null)
            return (null, $"{StudioNames.Folder(name)} needs both {StudioNames.AgentFile} and {StudioNames.GrantsFile}.", agentYaml);
        var (shape, problem) = AgentManifest.Read(name, agentYaml, grantsYaml, Ns);
        return (shape, problem, agentYaml);
    }

    /// <summary>An existing kagent agent the studio does not manage is never taken over (the seed, ticket stages, experiments).</summary>
    public async Task EnsureNotForeignAsync(string name, CancellationToken ct)
    {
        if (await kagent.GetAgentObjectAsync(Ns, name, ct) is { } existing
            && KAgentObjects.Label(existing, StudioNames.ManagedByLabel) != StudioNames.ManagedBy)
            throw new StudioApplyException($"kagent already runs an agent named '{name}' that the studio does not manage; choose another name.");
    }

    /// <summary>What memory holds for <paramref name="agentId"/> (D117, D121).</summary>
    public async Task<StudioPrincipal> PrincipalAsync(string agentId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return Require(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new StudioPrincipalQuery(agentId), ct));
    }

    /// <returns>False when the agent is suspended and was kept out of kagent.</returns>
    private async Task<bool> ApplyCoreAsync(AgentDraft draft, string actor, HashSet<string> servers, CancellationToken ct)
    {
        await EnsureNotForeignAsync(draft.Name, ct);
        var principal = await PrincipalAsync(draft.Name, ct);
        if (principal.Taken)
            throw new StudioApplyException($"Memory already has an agent '{draft.Name}' that the studio did not create; the studio never takes it over.");
        if (principal.Suspended)
        {
            // The owner's emergency stop (D121) outranks main: out of kagent, no memory server, until resumed.
            await RemoveFromKagentAsync(draft.Name, ct);
            servers.Remove($"{Ns}/{StudioNames.MemoryServer(draft.Name)}");
            return false;
        }
        await ApplyMemoryAsync(draft.Name, draft.Grants, actor, principal, servers, ct);
        await PutAgentAsync(KAgentObjects.WithLabels(AgentManifest.Agent(draft, Ns),
            (StudioNames.MergedLabel, "true"), (StudioNames.ManagedByLabel, StudioNames.ManagedBy)), ct);
        return true;
    }

    /// <summary>Creates the agent, or replaces its spec when it differs. Labels are set only at creation (kagent keeps them).</summary>
    private async Task PutAgentAsync(JsonObject agent, CancellationToken ct)
    {
        var name = agent["metadata"]!["name"]!.GetValue<string>();
        var existing = await kagent.GetAgentObjectAsync(Ns, name, ct);
        if (existing is null)
        {
            await kagent.CreateAgentAsync(agent, ct);
            logger.LogWarning("Studio created the kagent agent {Namespace}/{Agent}", Ns, name);
        }
        else if (KAgentObjects.Label(existing, StudioNames.ManagedByLabel) != StudioNames.ManagedBy)
            throw new StudioApplyException($"kagent already runs an agent named '{name}' that the studio does not manage.");
        else if (!KAgentObjects.SpecMatches(existing, agent))
        {
            await kagent.UpdateAgentAsync(agent, ct);
            logger.LogWarning("Studio updated the kagent agent {Namespace}/{Agent}", Ns, name);
        }
    }

    /// <summary>
    /// Grants into memory; with any grant, the agent's memory server holding the secret memory knows (D118). A new
    /// secret is issued only when memory has no studio secret, or kagent is not known to hold the one memory has (a
    /// failure between issue and create, a person's rotation, a revocation) or has no server for it. The order never
    /// leaves the two apart for good: the old server goes first (taking its Secret with it), then the issue, then the
    /// new server with a Secret named for that secret's fingerprint, then the mark that kagent holds it.
    /// </summary>
    private async Task ApplyMemoryAsync(string agentId, IReadOnlyList<StudioGrant> grants, string actor, StudioPrincipal principal,
        HashSet<string> servers, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var server = StudioNames.MemoryServer(agentId);
        var serverRef = $"{Ns}/{server}";
        if (grants.Count == 0)
        {
            Require(await mediator.Send(new RemoveStudioAccessCommand(actor, agentId), ct));
            if (servers.Remove(serverRef))
                await kagent.DeleteRemoteMcpServerAsync(Ns, server, ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(_options.MemoryMcpUrl))
            throw new StudioApplyException("Studio:MemoryMcpUrl is not set, so an agent with a memory grant cannot reach memory.");

        Require(await mediator.Send(new SetStudioGrantsCommand(actor, agentId, grants), ct));
        if (principal.SecretInPlace && servers.Contains(serverRef))
            return;

        if (servers.Remove(serverRef))
            await kagent.DeleteRemoteMcpServerAsync(Ns, server, ct);
        var issued = Require(await mediator.Send(new IssueStudioSecretCommand(actor, agentId), ct));
        var secretName = $"{server}-{issued.Fingerprint}";
        await kagent.CreateRemoteMcpServerAsync(KAgentObjects.MemoryServer(agentId, Ns, _options.MemoryMcpUrl, secretName),
            [(secretName, "token", $"Bearer {issued.Secret}")], ct);
        servers.Add(serverRef);
        Require(await mediator.Send(new MarkStudioSecretDeployedCommand(actor, agentId, issued.Fingerprint), ct));
        logger.LogWarning("Studio gave {Agent} memory secret {Fingerprint} in kagent's {Namespace}/{Server} (Secret {SecretName})",
            agentId, issued.Fingerprint, Ns, server, secretName);
    }

    private async Task RemoveAsync(string agentId, CancellationToken ct)
    {
        await RemoveFromKagentAsync(agentId, ct);
        using var scope = scopes.CreateScope();
        Require(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RemoveStudioAccessCommand(Actor, agentId), ct));
    }

    /// <summary>The agent (only if the studio manages it) and its memory server.</summary>
    private async Task RemoveFromKagentAsync(string agentId, CancellationToken ct)
    {
        if (await kagent.GetAgentObjectAsync(Ns, agentId, ct) is { } existing)
        {
            if (KAgentObjects.Label(existing, StudioNames.ManagedByLabel) != StudioNames.ManagedBy)
                return;
            await kagent.DeleteAgentAsync(Ns, agentId, ct);
            logger.LogWarning("Studio removed the kagent agent {Namespace}/{Agent}", Ns, agentId);
        }
        await kagent.DeleteRemoteMcpServerAsync(Ns, StudioNames.MemoryServer(agentId), ct);
    }

    /// <summary>A plugin's server, never an allow-listed one of the owner's (a stray file by that name changes nothing for it).</summary>
    private bool IsPluginServer(string server, IReadOnlyDictionary<string, string> pluginServers) =>
        pluginServers.ContainsKey(server) && !_options.McpServers.Contains(server);

    /// <returns>True when kagent runs the server as main describes it, with the header values memory holds.</returns>
    private async Task<bool> ApplyServerAsync(PluginServerEntry entry, HashSet<string> servers, Dictionary<string, PluginServerRecord> records, CancellationToken ct)
    {
        var (plugin, server) = (entry.Plugin, entry.Server);
        var reference = $"{Ns}/{server.Name}";
        var record = records.GetValueOrDefault(server.Name);
        switch (PluginServerStatus.Of(entry, record, servers.Contains(reference), Ns))
        {
            case PluginServerStates.Foreign:
                logger.LogError("Studio leaves the server {Namespace}/{Server} alone: kagent has one by that name that Skanyxx did not create for the plugin {Plugin}",
                    Ns, server.Name, plugin);
                return false;
            case PluginServerStates.NeedsSecret:
                logger.LogWarning("Studio holds the plugin server {Server} of {Plugin}: a header has no value yet (the owner enters it under Plugins)", server.Name, plugin);
                return false;
            case PluginServerStates.Deployed:
                return true;
        }

        using var scope = scopes.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var stored = server.Headers.Count == 0 ? null : Require(await mediator.Send(new PluginServerValuesQuery(server.Name), ct));
        if (stored is not null && (server.Headers.Any(h => !stored.Values.ContainsKey(h.Name)) || stored.Address != PluginServerUrl.Address(server.Url)))
        {
            logger.LogWarning("Studio holds the plugin server {Server} of {Plugin}: its stored header values cannot be read, or were entered for another address "
                + "(the owner enters them again)", server.Name, plugin);
            return false;
        }
        var values = server.Headers.ToDictionary(h => h.Name, h => stored!.Values[h.Name]);
        var fingerprint = stored?.Fingerprint ?? PluginHeaderFingerprint.None;
        var specHash = McpServerManifest.SpecHash(server, Ns);
        if (record is { Claimed: true } && servers.Contains(reference) && record.DeployedSpecHash == specHash && record.DeployedFingerprint == fingerprint)
            return true;

        // D118's order: claimed before anything is created (so a failure half way still leaves it the studio's), the old
        // server out (its Secret goes with it), the new one with a Secret named for the values, then the mark.
        Require(await mediator.Send(new ClaimPluginServerCommand(Actor, server.Name, plugin), ct));
        if (servers.Remove(reference))
            await kagent.DeleteRemoteMcpServerAsync(Ns, server.Name, ct);
        var secretName = $"{server.Name}-{fingerprint}";
        await kagent.CreateRemoteMcpServerAsync(KAgentObjects.PluginServer(server, plugin, Ns, secretName),
            [.. server.Headers.Select(h => (secretName, McpServerManifest.SecretKey(h.Name), values[h.Name]))], ct);
        servers.Add(reference);
        Require(await mediator.Send(new MarkPluginServerDeployedCommand(Actor, server.Name, plugin, fingerprint, specHash), ct));
        logger.LogWarning("Studio created the plugin server {Namespace}/{Server} of {Plugin} (spec {SpecHash}, header values {Fingerprint} in Secret {SecretName})",
            Ns, server.Name, plugin, specHash, fingerprint, secretName);
        return true;
    }

    private async Task RemoveServerAsync(string server, CancellationToken ct)
    {
        await kagent.DeleteRemoteMcpServerAsync(Ns, server, ct);
        using var scope = scopes.CreateScope();
        Require(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RemovePluginServerCommand(Actor, server), ct));
        logger.LogWarning("Studio removed the plugin server {Namespace}/{Server}: it is no longer in main of {Repo}", Ns, server, repo.Name);
    }

    public async Task<Dictionary<string, PluginServerRecord>> PluginRecordsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        return Require(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new PluginServerRecordsQuery(), ct)).ToDictionary(r => r.Server);
    }

    public async Task<HashSet<string>> ToolServersAsync(CancellationToken ct) => [.. (await ToolServerToolsAsync(ct)).Keys];

    /// <summary>kagent's tool servers (<c>ns/name</c>) and the tools it discovered on each; the first entry of a repeated ref wins (m14).</summary>
    public async Task<Dictionary<string, IReadOnlyList<string>>> ToolServerToolsAsync(CancellationToken ct) =>
        (await kagent.GetToolServerListAsync(ct))
            .Select(s => (Ref: Text(s?["ref"]), Tools: (IReadOnlyList<string>)[.. (s?["discoveredTools"] as JsonArray ?? []).Select(t => Text(t?["name"])).OfType<string>()]))
            .Where(s => s.Ref is not null)
            .GroupBy(s => s.Ref!)
            .ToDictionary(g => g.Key, g => g.First().Tools);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The lock for a person's request: waits for a running pass, then gives up (the caller says "try again").</summary>
    private async Task<IAsyncDisposable> HoldAsync(CancellationToken ct) =>
        await locks.TryAcquireAsync(TimeSpan.FromSeconds(_options.LockWaitSeconds), ct)
        ?? throw new TimeoutException($"Another studio change held the reconcile lock for {_options.LockWaitSeconds} s.");

    private static T Require<T>(Outcome<T> outcome) =>
        outcome.Status == OutcomeStatus.Ok ? outcome.Value! : throw new StudioApplyException(outcome.Message ?? "Memory refused the change.");
}
