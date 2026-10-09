using System.Text.Json.Nodes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Git;

namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <summary>
/// Our reconciler (D045): makes kagent and memory match main of the agent repo, through kagent's HTTP API — never
/// kubectl. For each agent folder in main: its grants into memory, its own memory server with a studio-issued secret
/// (only when it has a grant), and the Agent CR labelled merged (D100). An agent the studio manages that left main is
/// removed, behind the removal brake (D119); a preview whose pull request is no longer open is removed. Idempotent: an
/// agent that already matches is not written. One run at a time across every replica (D120): the periodic pass skips
/// when another holds the lock, everything else waits for it a bounded time.
/// </summary>
internal sealed class StudioReconciler(
    KAgentApiClient kagent, IAgentRepo repo, StudioRepoGuard guard, IStudioReconcileLock locks, IServiceScopeFactory scopes,
    IOptions<StudioOptions> options, ILogger<StudioReconciler> logger)
{
    public const string Actor = "studio:reconcile";

    private readonly StudioOptions _options = options.Value;

    private string Ns => _options.Namespace;

    /// <summary>The periodic pass. Agents that fail are logged and retried next time; the rest still apply.</summary>
    /// <returns>How many agents were not applied or removed; 0 when another replica is reconciling.</returns>
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        await using var held = await locks.TryAcquireAsync(TimeSpan.Zero, ct);
        if (held is null)
        {
            logger.LogInformation("Studio reconcile skipped: another replica holds the reconcile lock");
            return 0;
        }
        return await PassAsync(allowMassRemoval: false, ct);
    }

    /// <summary>The owner's confirmed pass (D119): the removal brake does not apply.</summary>
    public async Task<int> ConfirmedPassAsync(CancellationToken ct)
    {
        await using var held = await HoldAsync(ct);
        return await PassAsync(allowMassRemoval: true, ct);
    }

    private async Task<int> PassAsync(bool allowMassRemoval, CancellationToken ct)
    {
        await guard.RequireAsync(setup: true, ct);
        var paths = await repo.ListMainAsync(ct);
        var folders = MainFolders(paths);
        var desired = await ReadMainAsync(folders, ct);
        var servers = await ToolServersAsync(ct);
        var failures = 0;
        foreach (var draft in desired.Values)
        {
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

        var open = (await repo.OpenPullsAsync(ct)).Select(p => p.Number).ToHashSet();
        var managed = (await kagent.GetAgentsAsync(ct))
            .Where(a => a.Namespace == Ns && a.Labels.GetValueOrDefault(StudioNames.ManagedByLabel) == StudioNames.ManagedBy).ToList();
        var merged = managed.Where(a => !a.Labels.ContainsKey(StudioNames.PreviewLabel) && !a.Labels.ContainsKey(StudioNames.RoleLabel)).ToList();
        var gone = merged.Where(a => !folders.Contains(a.Name)).Select(a => a.Name).ToList();
        if (gone.Count > 0 && !allowMassRemoval && Brake(paths, gone.Count, merged.Count) is { } reason)
        {
            logger.LogError("Studio removes no agent this pass: {Reason} Would remove: {Agents}. {Hint}", reason, string.Join(", ", gone), StudioRepoGuard.ConfirmHint);
            failures += gone.Count;
            gone = [];
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
                logger.LogWarning(ex, "Studio could not remove {Agent}; retrying on the next pass", agent);
            }
        }

        await EnsureFactoryCoreAsync(ct);
        return failures;
    }

    /// <summary>Why this pass must not remove <paramref name="gone"/> of <paramref name="merged"/> agents, or null when it may.</summary>
    private string? Brake(IReadOnlyList<string> paths, int gone, int merged)
    {
        if (!paths.Any(p => p.StartsWith(StudioNames.AgentsFolder + "/", StringComparison.Ordinal)))
            return $"main of {repo.Name} has no {StudioNames.AgentsFolder}/ folder while kagent runs {merged} studio agent(s).";
        if (gone > _options.MaxRemovalsPerPass)
            return $"{gone} agents would leave at once (Studio:MaxRemovalsPerPass is {_options.MaxRemovalsPerPass}).";
        if (gone > 1 && gone * 2 > merged)
            return $"{gone} of {merged} studio agents would leave at once.";
        return null;
    }

    /// <summary>After a merge: the merged agent now (as <paramref name="actor"/>, for the audit), and its preview gone.</summary>
    /// <returns>False when the owner suspended the agent (D121): merged, but kept out of kagent.</returns>
    public async Task<bool> ApplyMergedAsync(AgentDraft draft, int number, string actor, CancellationToken ct)
    {
        await using var held = await HoldAsync(ct);
        var servers = await ToolServersAsync(ct);
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

    public async Task<HashSet<string>> MainFoldersAsync(CancellationToken ct) => MainFolders(await repo.ListMainAsync(ct));

    /// <summary>
    /// The valid agents among <paramref name="folders"/>; an invalid folder is logged and never applied (and the agent
    /// it describes is left as it runs, not removed).
    /// </summary>
    public async Task<Dictionary<string, AgentDraft>> ReadMainAsync(IEnumerable<string> folders, CancellationToken ct)
    {
        var drafts = new Dictionary<string, AgentDraft>();
        foreach (var name in folders)
        {
            var (draft, problems) = await ReadAgentAsync(name, _options.Git.Branch, ct);
            if (draft is null)
                logger.LogError("Studio skips {Folder} in main of {Repo}: {Problems}", StudioNames.Folder(name), repo.Name, string.Join(" ", problems));
            else
                drafts[name] = draft;
        }
        return drafts;
    }

    /// <summary>One agent's folder at a branch or commit, as a form, with every reason it is not a valid studio agent.</summary>
    public async Task<(AgentDraft? Draft, IReadOnlyList<string> Problems)> ReadAgentAsync(string name, string reference, CancellationToken ct)
    {
        if (!StudioNames.IsValidName(name))
            return (null, [$"'{name}' is not a valid agent name."]);
        string? agentYaml, grantsYaml;
        try
        {
            agentYaml = await repo.ReadAsync(StudioNames.AgentPath(name), reference, ct);
            grantsYaml = await repo.ReadAsync(StudioNames.GrantsPath(name), reference, ct);
        }
        catch (RepoFileTooLargeException)
        {
            return (null, [$"Agent files are at most {IAgentRepo.MaxFileBytes / 1024} KB."]);
        }
        if (agentYaml is null || grantsYaml is null)
            return (null, [$"{StudioNames.Folder(name)} needs both {StudioNames.AgentFile} and {StudioNames.GrantsFile}."]);
        var (draft, problem) = AgentManifest.Read(name, agentYaml, grantsYaml, Ns);
        if (draft is null)
            return (null, [problem!]);
        var problems = DraftRules.Check(draft, _options);
        return problems.Count == 0 ? (draft, []) : (null, problems);
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

    private async Task<HashSet<string>> ToolServersAsync(CancellationToken ct) =>
        [.. (await kagent.GetToolServerListAsync(ct))
            .Select(s => s is JsonObject o && o["ref"] is JsonValue v && v.TryGetValue<string>(out var r) ? r : null).OfType<string>()];

    /// <summary>The lock for a person's request: waits for a running pass, then gives up (the caller says "try again").</summary>
    private async Task<IAsyncDisposable> HoldAsync(CancellationToken ct) =>
        await locks.TryAcquireAsync(TimeSpan.FromSeconds(_options.LockWaitSeconds), ct)
        ?? throw new TimeoutException($"Another studio change held the reconcile lock for {_options.LockWaitSeconds} s.");

    private static T Require<T>(Outcome<T> outcome) =>
        outcome.Status == OutcomeStatus.Ok ? outcome.Value! : throw new StudioApplyException(outcome.Message ?? "Memory refused the change.");
}
