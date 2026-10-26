using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>Where a valid plugin server in main stands (<see cref="PluginServerStates"/>), from memory's record and kagent's list.</summary>
internal static class PluginServerStatus
{
    /// <returns>
    /// <see cref="PluginServerStates.NeedsSecret"/> also when the stored values were entered for another address (scheme,
    /// host, port or path) than the URL now names (D220): they are never sent there.
    /// </returns>
    public static string Of(PluginServerEntry entry, PluginServerRecord? record, bool inKagent, string ns)
    {
        var ours = record is { Claimed: true } && record.Plugin == entry.Plugin;
        if (inKagent && !ours)
            return PluginServerStates.Foreign;
        var declared = entry.Server.Headers.Select(h => h.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stored = record is not null && record.Plugin == entry.Plugin && record.ValueAddress == PluginServerUrl.Address(entry.Server.Url)
            ? record.ValueNames.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        if (!declared.IsSubsetOf(stored))
            return PluginServerStates.NeedsSecret;
        var fingerprint = declared.Count == 0 ? PluginHeaderFingerprint.None : record!.ValueFingerprint;
        return inKagent && ours && record!.DeployedFingerprint == fingerprint && record.DeployedSpecHash == McpServerManifest.SpecHash(entry.Server, ns)
            ? PluginServerStates.Deployed
            : PluginServerStates.Pending;
    }
}
