using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// Cloud tools are a paid feature (D364): a server on <see cref="StudioOptions.CloudToolServers"/> calls a cloud
/// provider's API, and an agent may use one only while <see cref="LicenceOptions.CloudTools"/> is on. Checked when a
/// form is proposed or applied and when a proposal is merged; agents already merged keep running if the licence ends.
/// </summary>
internal static class CloudToolRules
{
    public const string Reason = "licence";

    public static bool IsPaid(string server, StudioOptions options) => options.CloudToolServers.Contains(server, StringComparer.Ordinal);

    public static IReadOnlyList<string> Check(AgentDraft draft, StudioOptions options, LicenceOptions licence) =>
        licence.CloudTools
            ? []
            : [.. draft.McpTools.Select(t => t.Server).Where(s => IsPaid(s, options)).Distinct().Order(StringComparer.Ordinal)
                .Select(s => $"Tools: '{s}' calls a cloud provider's API, and cloud tools are a paid feature that is not turned on. "
                    + "Remove it, or ask the owner to turn on the cloud tools licence (Skanyxx:Licence:CloudTools).")];
}
