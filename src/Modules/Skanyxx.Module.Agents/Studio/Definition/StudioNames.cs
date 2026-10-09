using System.Text.RegularExpressions;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>Names and labels the studio owns in git and kagent.</summary>
internal static partial class StudioNames
{
    /// <summary>Set by the reconciler on merge; Chat lists only agents carrying it (D100, D031).</summary>
    public const string MergedLabel = "skanyxx.dev/merged";

    /// <summary>On every object the studio creates in kagent. An agent without it is never adopted or changed.</summary>
    public const string ManagedByLabel = "skanyxx.dev/managed-by";

    public const string ManagedBy = "skanyxx-studio";

    /// <summary>On a preview agent: the pull request it previews (D030). Never together with <see cref="MergedLabel"/>.</summary>
    public const string PreviewLabel = "skanyxx.dev/preview";

    /// <summary>On the factory agent (D5).</summary>
    public const string RoleLabel = "skanyxx.dev/role";

    public const string FactoryAgent = "skanyxx-factory";

    public const string AgentsFolder = "agents";

    public const string AgentFile = "agent.yaml";

    public const string GrantsFile = "grants.yaml";

    /// <summary>
    /// The longest agent name: <c>skanyxx-memory-preview-&lt;pr&gt;-&lt;name&gt;</c> must stay a Kubernetes name
    /// (63): the prefix with a 6-digit PR number (<c>skanyxx-memory-preview-999999-</c>) is 30 characters.
    /// </summary>
    public const int MaxNameLength = 33;

    public const string MemorySearch = "memory_search";

    public const string MemoryUpsert = "memory_upsert";

    /// <summary>Names a person may not take: the seed is the owner's, and these prefixes are the studio's own objects.</summary>
    public static bool IsReserved(string name) =>
        name == "seed" || name == FactoryAgent || name.StartsWith("preview-", StringComparison.Ordinal)
        || name.StartsWith("skanyxx-", StringComparison.Ordinal);

    /// <summary>
    /// A studio agent name: a Kubernetes name that starts with a letter, because kagent derives a Service from it and a
    /// Service name is a DNS-1035 label.
    /// </summary>
    public static bool IsValidName(string? name) => name is not null && name.Length <= MaxNameLength && AgentName().IsMatch(name);

    /// <summary>A Kubernetes object name (RFC 1123 label).</summary>
    public static bool IsKubeName(string? name) => name is not null && name.Length <= 63 && KubeName().IsMatch(name);

    public const string MemoryServerPrefix = "skanyxx-memory-";

    public static string MemoryServer(string agent) => MemoryServerPrefix + agent;

    public static string PreviewAgent(int number, string agent) => $"preview-{number}-{agent}";

    /// <summary>The proposal number of a preview agent's name, or null when it is not one.</summary>
    public static int? PreviewNumber(string agent) =>
        agent.StartsWith("preview-", StringComparison.Ordinal) && agent.Split('-') is [_, var number, ..]
            && int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n : null;

    public static string Folder(string agent) => $"{AgentsFolder}/{agent}";

    public static string AgentPath(string agent) => $"{Folder(agent)}/{AgentFile}";

    public static string GrantsPath(string agent) => $"{Folder(agent)}/{GrantsFile}";

    [GeneratedRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?\\z")]
    private static partial Regex KubeName();

    [GeneratedRegex("^[a-z]([-a-z0-9]*[a-z0-9])?\\z")]
    private static partial Regex AgentName();
}
