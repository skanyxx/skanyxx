using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// Uninstall = a pull request deleting the plugin's folder and its agents' folders (P8, P11: builders and the owner).
/// After a supervisor's merge the reconciler removes the agents, then the servers, under the removal brake. Refused while
/// an agent outside the plugin still uses one of its servers (D219): that agent would be left bound to nothing.
/// </summary>
internal sealed class UninstallPluginHandler(
    IAgentRepo repo, StudioRepoGuard guard, StudioReconciler reconciler, PluginRepoReader plugins, IOptions<StudioOptions> options,
    ILogger<UninstallPluginHandler> logger)
    : IRequestHandler<UninstallPluginCommand, Outcome<StudioResult>>
{
    public async Task<Outcome<StudioResult>> Handle(UninstallPluginCommand command, CancellationToken ct)
    {
        var (user, name) = (command.User, command.Plugin);
        if (!user.CanPropose)
            return Outcome<StudioResult>.Forbidden(StudioFailure.NotBuilder);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured);
        try
        {
            await guard.RequireAsync(setup: false, ct);
            var mainPaths = await repo.ListMainAsync(ct);
            var (plugin, problem) = await plugins.ReadPluginAsync(name, options.Value.Git.Branch, ct);
            if (plugin is null)
                return PluginRepoReader.PluginFolders(mainPaths).Contains(name)
                    ? StudioFailure.Conflict<StudioResult>($"The plugin '{name}' in main is not valid ({problem}); it cannot be uninstalled by the studio.", "invalid")
                    : Outcome<StudioResult>.NotFound($"No plugin '{name}' in main.");

            var others = StudioReconciler.MainFolders(mainPaths).Except(plugin.Agents);
            if (PluginUsers.Problem(plugin.Servers, await reconciler.ServerUsersAsync(others, ct)) is { } used)
                return StudioFailure.Conflict<StudioResult>(used, "in-use");

            var open = await repo.OpenPullsAsync(ct);
            if (await PluginPending.FindAsync(repo, open, name, plugin.Agents, plugin.Servers, ct) is { } pending)
                return StudioFailure.Conflict<StudioResult>(pending, "pending");
            if (open.Count(p => ProposalTrailer.ProposedBy(p.Body) == user.UserId) >= options.Value.MaxOpenProposalsPerBuilder)
                return StudioFailure.Conflict<StudioResult>(
                    $"You have {options.Value.MaxOpenProposalsPerBuilder} open proposals; close one, or wait for a supervisor, before proposing another.", "too-many");

            var owned = plugin.Agents.Select(a => StudioNames.Folder(a) + "/").Append(StudioNames.PluginFolder(name) + "/").ToList();
            List<RepoChange> changes = [.. mainPaths.Where(p => owned.Any(o => p.StartsWith(o, StringComparison.Ordinal))).Select(RepoChange.Delete)];
            if (changes.Count > StudioOptions.MaxPluginFiles)
                return StudioFailure.Conflict<StudioResult>(
                    $"Removing this plugin changes {changes.Count} files; a plugin proposal changes at most {StudioOptions.MaxPluginFiles}.", "too-large");

            var branch = $"plugins/{name}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var title = $"Uninstall plugin {name}";
            var description = $"Removes the plugin {name}: agents {List(plugin.Agents)}; servers {List(plugin.Servers)}.";
            var pull = await repo.ProposeAsync(branch, changes, $"{title}\n\nProposed-by: {user.UserId}", user.Name, user.Email, title,
                ProposalTrailer.WritePlugin(name, user.UserId!, description, []), ct);
            logger.LogWarning("Plugin proposal #{Number} ({Title}) opened by {ActorUserId} in {Repo}", pull.Number, title, user.UserId, repo.Name);
            return Outcome<StudioResult>.Created(new StudioResult(
                $"Proposed as #{pull.Number}. Once a supervisor merges it, the reconciler removes the plugin's agents, then its servers.", pull.Number, null));
        }
        catch (StudioRepoException ex)
        {
            return StudioFailure.Conflict<StudioResult>(ex.Message, StudioFailure.RepoReason);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Plugin uninstall of {Plugin} by {ActorUserId} failed", name, user.UserId);
            return Outcome<StudioResult>.Unavailable(StudioFailure.Unavailable);
        }
    }

    private static string List(IReadOnlyList<string> names) => names.Count == 0 ? "none" : string.Join(", ", names);
}
