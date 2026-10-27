using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>
/// Which agents the Agents page may change (D252). A studio agent changes only by a pull request: a direct edit would
/// diverge from git and the reconciler would put main back. A system agent is Skanyxx's own and never changes here.
/// </summary>
internal static class AgentProtection
{
    /// <summary>The managed-by value on agents created on the Agents page (the studio's is <see cref="StudioNames.ManagedBy"/>).</summary>
    public const string ManagedBy = "skanyxx-agents";

    private const string PartOfLabel = "app.kubernetes.io/part-of";

    public static string SourceOf(string name, IReadOnlyDictionary<string, string> labels) =>
        labels.GetValueOrDefault(StudioNames.ManagedByLabel) switch
        {
            _ when IsSystem(name, labels) => AgentSource.System,
            StudioNames.ManagedBy => AgentSource.Studio,
            ManagedBy => AgentSource.Direct,
            _ => AgentSource.External
        };

    /// <summary>Why <paramref name="name"/> may not be edited or deleted directly; null when it may.</summary>
    public static string? Refusal(string name, IReadOnlyDictionary<string, string> labels) => SourceOf(name, labels) switch
    {
        AgentSource.System => $"'{name}' is a system agent that Skanyxx installs; it cannot be edited or deleted on the Agents page.",
        AgentSource.Studio => $"'{name}' is managed by Studio: git ({StudioNames.AgentPath(name)}) is its source of truth. "
            + "Change or remove it with a Studio proposal; a direct edit would silently diverge from git.",
        _ => null
    };

    /// <summary>
    /// Whether another agent may call it as a tool (D256): never a system or studio agent, whose memory identity, grants
    /// or studio powers would then answer to whoever chats with the caller.
    /// </summary>
    public static bool IsCallable(string name, IReadOnlyDictionary<string, string> labels) =>
        SourceOf(name, labels) is AgentSource.Direct or AgentSource.External;

    private static bool IsSystem(string name, IReadOnlyDictionary<string, string> labels) =>
        StudioNames.IsReserved(name)
        || labels.ContainsKey(StudioNames.RoleLabel)
        || labels.GetValueOrDefault(PartOfLabel)?.StartsWith("skanyxx", StringComparison.Ordinal) == true;
}
