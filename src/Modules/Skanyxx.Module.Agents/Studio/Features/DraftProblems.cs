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
        problems.AddRange(Licensing(draft, await MainAsync(draft, pluginServers, ct)));
        return problems;
    }

    /// <summary>
    /// What a valid shape may still not hold: paid cloud tools without the licence (except those <paramref name="main"/>
    /// already gives the agent, unchanged), and credentials in its text.
    /// </summary>
    public IReadOnlyList<string> Licensing(AgentDraft draft, AgentDraft? main) =>
        [.. CloudToolRules.Check(draft, options.Value, cloudTools.IsOn, main), .. TextSecrets.Check(draft)];

    /// <summary>
    /// The agent as main has it, read only when the cloud tools rule needs it (it is off and the draft names a paid
    /// server). Null when there is none or main cannot be read: then a kept paid server is refused like a new one.
    /// </summary>
    public async Task<AgentDraft?> MainAsync(AgentDraft draft, IReadOnlyDictionary<string, string> pluginServers, CancellationToken ct)
    {
        if (cloudTools.IsOn || !options.Value.Configured || !draft.McpTools.Any(t => CloudToolRules.IsPaid(t.Server, options.Value)))
            return null;
        try
        {
            return (await reconciler.ReadAgentAsync(draft.Name, options.Value.Git.Branch, pluginServers, [], ct)).Draft;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or StudioApplyException)
        {
            return null;
        }
    }
}
