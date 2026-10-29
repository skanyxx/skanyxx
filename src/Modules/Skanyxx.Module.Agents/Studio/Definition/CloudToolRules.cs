using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// Cloud tools are a paid feature (D364): a server on <see cref="StudioOptions.CloudToolServers"/> calls a cloud
/// provider's API, and an agent may use one only while <see cref="CloudToolsGate"/> is on (licence and the Settings
/// switch). Checked when a form is proposed or applied, when a proposal is merged and on the Agents page's direct save;
/// agents already merged keep running if it goes off.
/// </summary>
internal static class CloudToolRules
{
    public const string Reason = "licence";

    public static bool IsPaid(string server, StudioOptions options) => options.CloudToolServers.Contains(server, StringComparer.Ordinal);

    /// <param name="main">The agent as main has it, if it exists: a paid server main already gives it, with the same
    /// tools, is kept rather than added, as on the Agents page (D388), so editing the rest of the agent stays possible.</param>
    public static IReadOnlyList<string> Check(AgentDraft draft, StudioOptions options, bool cloudToolsOn, AgentDraft? main = null) =>
        cloudToolsOn ? [] : [.. Refusals(draft.McpTools.Where(t => !Kept(t, main)).Select(t => t.Server), options)];

    private static bool Kept(McpToolChoice choice, AgentDraft? main) =>
        main?.McpTools.FirstOrDefault(m => m.Server == choice.Server) is { } before && before.Tools.ToHashSet().SetEquals(choice.Tools);

    /// <summary>One sentence per paid server in <paramref name="servers"/>, for when cloud tools are off.</summary>
    public static IEnumerable<string> Refusals(IEnumerable<string> servers, StudioOptions options) =>
        servers.Where(s => IsPaid(s, options)).Distinct().Order(StringComparer.Ordinal).Select(Refusal);

    public static string Refusal(string server) =>
        $"Tools: '{server}' calls a cloud provider's API, and cloud tools are a paid feature that is not turned on. "
        + "Remove it, or ask the owner to turn cloud tools on (the licence Skanyxx:Licence:CloudTools and Settings → Features).";
}
