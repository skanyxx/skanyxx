using MediatR;
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
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// Install = one pull request (P1, P8): <c>plugins/&lt;name&gt;/plugin.yaml</c>, its servers and its agents' folders, with
/// what a new version dropped removed. Refused when a name it brings belongs to someone else (another plugin, a studio
/// agent no plugin owns, kagent or memory), when the plugin or one of its agents already has an open proposal, when its
/// model is not in kagent, or when it would change more than <see cref="StudioOptions.MaxPluginFiles"/> files. A literal
/// header value goes to memory (encrypted, never to git) before the proposal opens; one the server already has is kept.
/// </summary>
internal sealed class InstallPluginHandler(
    IAgentRepo repo, StudioRepoGuard guard, StudioReconciler reconciler, PluginRepoReader plugins, KAgentApiClient kagent, IMediator mediator,
    IOptions<StudioOptions> options, ILogger<InstallPluginHandler> logger)
    : IRequestHandler<InstallPluginCommand, Outcome<StudioResult>>
{
    /// <summary>Shorter literals are not credentials (<c>x-posthog-mcp-consumer: plugin</c>) and would match ordinary words.</summary>
    private const int MinCheckedValueLength = 12;

    private StudioOptions Options => options.Value;

    public async Task<Outcome<StudioResult>> Handle(InstallPluginCommand command, CancellationToken ct)
    {
        var (user, draft) = (command.User, command.Draft);
        if (!PluginAccess.CanInstall(user, draft.Source.Kind))
        {
            logger.LogWarning("Plugin install of {Plugin} ({Kind}) refused for {ActorUserId}", draft.Name, draft.Source.Kind, user.UserId);
            return Outcome<StudioResult>.Forbidden(PluginAccess.NotInstaller);
        }
        if (!Options.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured);
        var ns = Options.Namespace;
        try
        {
            await guard.RequireAsync(setup: false, ct);
            var models = await kagent.GetModelConfigRefsAsync(ct);
            if (draft.Agents.FirstOrDefault(a => !models.Contains($"{ns}/{a.ModelConfig}")) is { } noModel)
                return StudioFailure.Conflict<StudioResult>($"kagent has no model '{noModel.ModelConfig}' in {ns} (for the agent '{noModel.Name}').");

            var mainPaths = await repo.ListMainAsync(ct);
            var tree = await plugins.ReadAsync(mainPaths, Options.Git.Branch, log: false, ct);
            if (tree.Problems.TryGetValue(draft.Name, out var invalid))
                return StudioFailure.Conflict<StudioResult>($"The plugin '{draft.Name}' in main is not valid ({invalid}); it cannot be updated by an install.", "invalid");
            var current = tree.Plugins.GetValueOrDefault(draft.Name);
            if (await TakenAsync(draft, tree, StudioReconciler.MainFolders(mainPaths), ct) is { } taken)
                return StudioFailure.Conflict<StudioResult>(taken, "taken");

            var open = await repo.OpenPullsAsync(ct);
            if (await PluginPending.FindAsync(repo, open, draft.Name, [.. draft.Agents.Select(a => a.Name)], [.. draft.Servers.Select(s => s.Name)], ct) is { } pending)
                return StudioFailure.Conflict<StudioResult>(pending, "pending");
            if (open.Count(p => ProposalTrailer.ProposedBy(p.Body) == user.UserId) >= Options.MaxOpenProposalsPerBuilder)
                return StudioFailure.Conflict<StudioResult>(
                    $"You have {Options.MaxOpenProposalsPerBuilder} open proposals; close one, or wait for a supervisor, before proposing another.", "too-many");

            var files = PluginRender.Files(draft, ns);
            if (Leak(draft, files) is { } leak)
                return StudioFailure.Conflict<StudioResult>(leak, "secret");
            var changes = await ChangesAsync(draft, current, files, mainPaths, ct);
            if (changes.Count == 0)
                return StudioFailure.Conflict<StudioResult>($"The plugin '{draft.Name}' in main is already exactly this.", "unchanged");
            if (changes.Count > StudioOptions.MaxPluginFiles)
                return StudioFailure.Conflict<StudioResult>(
                    $"This plugin would change {changes.Count} files; a plugin proposal changes at most {StudioOptions.MaxPluginFiles}.", "too-large");

            var report = draft.Notes.Select(n => $"- {n.Item}: {n.Status} ({n.Reason})").ToList();
            if (MixedServers(draft) is { } mixed)
                report.Add(mixed);
            if (await StoreValuesAsync(user, draft, report, ct) is { } refused)
                return refused;

            var branch = $"plugins/{draft.Name}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var title = current is null ? $"Install plugin {draft.Name}" : $"Update plugin {draft.Name}";
            var pull = await repo.ProposeAsync(branch, changes, $"{title}\n\nProposed-by: {user.UserId}", user.Name, user.Email, title,
                ProposalTrailer.WritePlugin(draft.Name, user.UserId!, draft.Description, report), ct);
            logger.LogWarning("Plugin proposal #{Number} ({Title}, {Kind} {Digest}) opened by {ActorUserId} in {Repo}: {Files} file(s)",
                pull.Number, title, draft.Source.Kind, draft.Source.Digest, user.UserId, repo.Name, changes.Count);
            return Outcome<StudioResult>.Created(new StudioResult(
                $"Proposed as #{pull.Number}. {PluginAccess.Merger(draft.Source.Kind)} reviews and merges it; until then nothing of it is live.", pull.Number, null));
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
            logger.LogWarning(ex, "Plugin install of {Plugin} by {ActorUserId} failed", draft.Name, user.UserId);
            return Outcome<StudioResult>.Unavailable(StudioFailure.Unavailable);
        }
    }

    /// <summary>Why a name the plugin brings is someone else's, or null.</summary>
    private async Task<string?> TakenAsync(PluginDraft draft, RepoPlugins tree, HashSet<string> mainFolders, CancellationToken ct)
    {
        foreach (var agent in draft.Agents.Select(a => a.Name))
        {
            var owner = tree.OwnerOf(agent);
            if (owner is not null && owner != draft.Name)
                return $"The agent '{agent}' belongs to the plugin '{owner}'.";
            if (owner is null && mainFolders.Contains(agent))
                return $"The agent '{agent}' is a studio agent no plugin owns; a plugin cannot take it over.";
            await reconciler.EnsureNotForeignAsync(agent, ct);
            if ((await reconciler.PrincipalAsync(agent, ct)).Taken)
                return StudioFailure.NameTaken(agent);
        }
        var kagentServers = await reconciler.ToolServersAsync(ct);
        var records = await reconciler.PluginRecordsAsync(ct);
        foreach (var server in draft.Servers.Select(s => s.Name))
        {
            if (tree.ServerOwnerOf(server) is { } owner && owner != draft.Name)
                return $"The server '{server}' belongs to the plugin '{owner}'.";
            var record = records.GetValueOrDefault(server);
            if (record is { Claimed: true } && record.Plugin != draft.Name)
                return $"The server '{server}' runs in kagent for the plugin '{record.Plugin}'.";
            if (kagentServers.Contains($"{Options.Namespace}/{server}") && record is not { Claimed: true })
                return $"kagent already has a server '{server}' that Skanyxx did not create; choose another name.";
        }
        return null;
    }

    /// <summary>
    /// One party's open servers beside its credentialed ones: the plugin's agents use them together, so content from an
    /// open server can steer what is done with the credentials (sec3 R5). The one-plugin rule does not separate them.
    /// </summary>
    private static string? MixedServers(PluginDraft draft)
    {
        var open = draft.Servers.Where(s => s.Headers.Count == 0).Select(s => s.Name).ToList();
        var credentialed = draft.Servers.Where(s => s.Headers.Count > 0).Select(s => s.Name).ToList();
        return open.Count > 0 && credentialed.Count > 0
            ? $"- {draft.Name}: its agents may use the servers without headers ({string.Join(", ", open)}) together with the credentialed ones "
                + $"({string.Join(", ", credentialed)}); what an open server returns can steer what the agents do with those credentials, so vet them as one"
            : null;
    }

    /// <summary>P3: no header value, and nothing that looks like a credential, in any file bound for git.</summary>
    private static string? Leak(PluginDraft draft, Dictionary<string, string> files)
    {
        var values = draft.Servers.SelectMany(s => s.Headers).Select(h => h.Value).OfType<string>().Where(v => v.Length >= MinCheckedValueLength).ToList();
        foreach (var (path, text) in files)
        {
            if (values.Any(v => text.Contains(v, StringComparison.Ordinal)))
                return $"{path} would hold a header value; the plugin is refused (header values go to the server's Secret, never to git).";
            if (SecretScan.Find(text) is { } secret)
                return $"{path} seems to hold {secret}; the plugin is refused (header values go to the server's Secret, never to git).";
        }
        return null;
    }

    /// <summary>What differs from main: changed or new files, and every file of the plugin (or its dropped agents) the new version no longer has.</summary>
    private async Task<List<RepoChange>> ChangesAsync(PluginDraft draft, InstalledPlugin? current, Dictionary<string, string> files, IReadOnlyList<string> mainPaths,
        CancellationToken ct)
    {
        var changes = new List<RepoChange>();
        foreach (var (path, content) in files)
        {
            var exists = mainPaths.Contains(path);
            string? text;
            try
            {
                text = exists ? await repo.ReadAsync(path, Options.Git.Branch, ct) : null;
            }
            catch (RepoFileTooLargeException)
            {
                text = null;
            }
            if (text != content)
                changes.Add(new RepoChange(path, content, exists));
        }
        var dropped = (current?.Agents ?? []).Except(draft.Agents.Select(a => a.Name)).Select(a => StudioNames.Folder(a) + "/")
            .Append(StudioNames.PluginFolder(draft.Name) + "/").ToList();
        changes.AddRange(mainPaths.Where(p => !files.ContainsKey(p) && dropped.Any(d => p.StartsWith(d, StringComparison.Ordinal))).Select(RepoChange.Delete));
        return changes;
    }

    /// <summary>
    /// Literal values the plugin carried, stored before the proposal opens and never written to git. A header that already
    /// has a value for the same address keeps it (a merge is what changes a running server), and the report says so. A
    /// server whose address moves keeps the values held for its old address untouched until the move is merged: nothing
    /// is stored for it now, and the owner enters its values (the literal ones included, named in the report) after the
    /// merge (D220). Every server with headers is sent them on behalf of every chat user of the plugin's agents, which the
    /// report says too.
    /// </summary>
    private async Task<Outcome<StudioResult>?> StoreValuesAsync(StudioUser user, PluginDraft draft, List<string> report, CancellationToken ct)
    {
        var records = await reconciler.PluginRecordsAsync(ct);
        foreach (var server in draft.Servers)
        {
            var values = server.Headers.Where(h => h.Value is not null).ToDictionary(h => h.Name, h => h.Value!);
            var address = PluginServerUrl.Address(server.Url);
            var record = records.GetValueOrDefault(server.Name) is { } r && r.Plugin == draft.Name && r.ValueNames.Count > 0 ? r : null;
            // Only a claimed row's values belong to a running server; an unclaimed one at another address is replaced.
            var moved = record is { Claimed: true } && record.ValueAddress != address;
            if (moved)
                report.Add($"- {server.Name}: its address moves from {record!.ValueAddress ?? "an unrecorded address"} to {address}; the values Skanyxx holds stay "
                    + $"with the running server and are never sent there, so the owner enters the values after merge ({string.Join(", ", server.Headers.Select(h => h.Name))})");
            var stored = record is not null && record.ValueAddress == address ? record.ValueNames : [];
            foreach (var header in server.Headers)
                if (stored.Contains(header.Name, StringComparer.OrdinalIgnoreCase))
                    report.Add($"- {server.Name} {header.Name}: kept (Skanyxx already holds a value; the owner replaces it under Plugins)");
                else if (header.Template is not null && !moved)
                    report.Add($"- {server.Name} {header.Name}: needs a value from the owner ({header.Template}); the server waits for it");
            if (server.Headers.Count > 0)
                report.Add($"- {server.Name}: every chat user of this plugin's agents acts at {address} with these header values; give it a least-privilege token");
            if (values.Count == 0 || moved)
                continue;
            var outcome = await mediator.Send(new StorePluginServerValuesCommand(user.UserId!, server.Name, draft.Name, values, KeepExisting: true, address), ct);
            if (outcome.Status != OutcomeStatus.Ok)
                return StudioFailure.Conflict<StudioResult>(outcome.Message ?? "Memory refused the server's header values.", outcome.Reason ?? "taken");
        }
        return null;
    }
}
