using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// The owner's header values for a plugin server in main (P6), like a ModelConfig's key: only names its plugin declares,
/// stored encrypted by memory for the address the server's URL names (D220), then the reconciler is woken to (re)create the
/// server with them. Values, and the fingerprint naming them, are never logged or returned.
/// </summary>
internal sealed class SetPluginServerSecretsHandler(
    IAgentRepo repo, StudioRepoGuard guard, PluginRepoReader plugins, IMediator mediator, StudioSignal signal, IOptions<StudioOptions> options,
    ILogger<SetPluginServerSecretsHandler> logger)
    : IRequestHandler<SetPluginServerSecretsCommand, Outcome<StudioResult>>
{
    public async Task<Outcome<StudioResult>> Handle(SetPluginServerSecretsCommand command, CancellationToken ct)
    {
        var user = command.User;
        if (!user.IsOwner)
        {
            logger.LogWarning("Plugin server values for {Server} refused for {ActorUserId}: not the owner", command.Server, user.UserId);
            return Outcome<StudioResult>.Forbidden(StudioFailure.NotOwner);
        }
        if (!options.Value.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured);
        try
        {
            await guard.RequireAsync(setup: false, ct);
            var tree = await plugins.ReadAsync(await repo.ListMainAsync(ct), options.Value.Git.Branch, log: false, ct);
            if (!tree.Servers.TryGetValue(command.Server, out var entry))
                return Outcome<StudioResult>.NotFound(tree.ServerProblems.TryGetValue(command.Server, out var problem)
                    ? $"The plugin server '{command.Server}' in main is not valid: {problem}"
                    : $"No plugin server '{command.Server}' in main.");
            var declared = entry.Server.Headers.Select(h => h.Name).ToList();
            var values = new Dictionary<string, string>();
            foreach (var (name, value) in command.Values)
            {
                if (declared.FirstOrDefault(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase)) is not { } header)
                    return StudioFailure.Conflict<StudioResult>(
                        $"The server '{command.Server}' declares no header '{name}' (it declares {(declared.Count == 0 ? "none" : string.Join(", ", declared))}).", "undeclared");
                values[header] = value;
            }
            var stored = await mediator.Send(new StorePluginServerValuesCommand(user.UserId!, command.Server, entry.Plugin, values, KeepExisting: false,
                PluginServerUrl.Address(entry.Server.Url)), ct);
            if (stored.Status != OutcomeStatus.Ok)
                return StudioFailure.Conflict<StudioResult>(stored.Message ?? "Memory refused the values.", stored.Reason);
            signal.Poke();
            return Outcome<StudioResult>.Ok(new StudioResult(
                $"Stored {values.Count} header value(s) for {command.Server}; the reconciler gives them to kagent now.", null, null));
        }
        catch (StudioApplyException ex)
        {
            return StudioFailure.Conflict<StudioResult>(ex.Message, StudioFailure.RepoReason);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Plugin server values for {Server} could not be stored", command.Server);
            return Outcome<StudioResult>.Unavailable(StudioFailure.Unavailable);
        }
    }
}
