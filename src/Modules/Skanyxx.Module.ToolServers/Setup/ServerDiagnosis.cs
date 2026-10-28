using Skanyxx.Module.ToolServers.Data;
using Skanyxx.Module.ToolServers.Definition;

namespace Skanyxx.Module.ToolServers.Setup;

/// <summary>
/// What is not working on one server, from what kagent lists and what Skanyxx sent it. kagent 0.10.2 lists no status
/// or error for a server, so a server with no tools gets every likely cause, the most likely first.
/// </summary>
internal static class ServerDiagnosis
{
    public static IReadOnlyList<SetupFix> Of(FixFacts facts, ToolServerRecord? record, bool listed)
    {
        if (facts.Kind == ToolServerKinds.Stdio)
            return [ToolServerFixes.StdioUnsupported(facts)];
        if (!listed)
            return [ToolServerFixes.NotRegistered(facts)];
        if (facts.ToolCount > 0)
            return [];
        if (record is null)
            return [ToolServerFixes.DiscoveryFailed(facts)];
        if (facts.LoopbackUrl)
            return [ToolServerFixes.ServerUnreachable(facts)];
        var fixes = new List<SetupFix>();
        if (facts.SecretHeaders.Count > 0)
            fixes.Add(ToolServerFixes.HeaderSecret(facts));
        if (record.Url.StartsWith("https:", StringComparison.Ordinal))
            fixes.Add(ToolServerFixes.Tls(facts));
        fixes.Add(ToolServerFixes.ServerUnreachable(facts));
        return fixes;
    }

    public static ServerStatus Status(IReadOnlyList<SetupFix> fixes, int? toolCount) => fixes.Count == 0
        ? new(ServerStatus.Ready, $"Ready: {toolCount} tool{(toolCount == 1 ? "" : "s")}")
        : new(fixes[0].Code, fixes[0].Code switch
        {
            FixCodes.StdioUnsupported => "Stdio server: listed, not managed here",
            FixCodes.NotRegistered => "Not registered: kagent does not list this server yet",
            FixCodes.DiscoveryFailed => "No tools: kagent could not discover any",
            FixCodes.HeaderSecret => "No tools: a header Secret may be missing",
            FixCodes.Tls => "No tools: check the certificate and URL",
            _ => "Unreachable: kagent cannot reach the server"
        });

    public static readonly ServerStatus Unknown = new(ServerStatus.Unknown, "Unknown: kagent did not answer");
}
