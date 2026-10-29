using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Reconcile;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>
/// Every reason a form could not be proposed, in plain words (D361): the form's rules (<see cref="DraftRules"/>, with the
/// plugin servers merged in main when the form names a server off the allow-list), the cloud tools licence (D364) and
/// credentials typed into its text. One place, so the YAML preview, a proposal and CI say the same thing.
/// </summary>
internal sealed class DraftProblems(StudioReconciler reconciler, IOptions<StudioOptions> options, CloudToolsGate cloudTools)
{
    public const string PluginServersUnreadable = "The plugin servers in main cannot be read right now; try again in a moment.";

    public async Task<IReadOnlyList<string>> CheckAsync(AgentDraft draft, CancellationToken ct)
    {
        var o = options.Value;
        var problems = new List<string>();
        IReadOnlyDictionary<string, string> pluginServers = new Dictionary<string, string>();
        if (o.Configured && draft.McpTools.Any(t => !o.McpServers.Contains(t.Server)))
        {
            try
            {
                pluginServers = await reconciler.MainPluginServersAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException or StudioApplyException)
            {
                problems.Add(PluginServersUnreadable);
            }
        }
        problems.AddRange(DraftRules.Check(draft, o, pluginServers));
        problems.AddRange(Licensing(draft));
        return problems;
    }

    /// <summary>What a valid shape may still not hold: paid cloud tools without the licence, and credentials in its text.</summary>
    public IReadOnlyList<string> Licensing(AgentDraft draft) =>
        [.. CloudToolRules.Check(draft, options.Value, cloudTools.IsOn), .. TextSecrets.Check(draft)];
}
