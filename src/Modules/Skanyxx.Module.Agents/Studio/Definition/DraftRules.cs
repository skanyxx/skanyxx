using System.Globalization;
using System.Text.RegularExpressions;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// What a studio agent may contain (D029), checked on the form when it is proposed and on the files again when they are
/// merged or reconciled. Returns every problem, in words for the builder.
/// </summary>
internal static partial class DraftRules
{
    public const int MaxDescription = 500;
    public const int MaxInstructions = 16_000;
    public const int MaxSkills = 20;
    public const int MaxMcpTools = 50;
    public const int MaxGrants = 50;
    public const int MaxTtlDays = 365;

    private static readonly Dictionary<string, string> NoPluginServers = [];

    /// <param name="pluginServers">
    /// The plugin servers an agent may also use (P7), each with the plugin that has it: those declared in main and, for a
    /// plugin proposal, in its head.
    /// </param>
    /// <param name="ownServers">
    /// The servers of the plugin that owns this agent (none for any other agent). Only these may be used with no tool names
    /// (= every tool, now and later). Any other agent names its tools on a plugin's (third-party) server, and uses servers
    /// of at most one plugin, never together with an allow-listed server, memory grants or kagent TTL memory: a third
    /// party's tool descriptions and results must reach neither company memory nor another credentialed server, which
    /// could carry private data out through the third party (D219).
    /// </param>
    public static IReadOnlyList<string> Check(AgentDraft draft, StudioOptions options, IReadOnlyDictionary<string, string>? pluginServers = null,
        IReadOnlyCollection<string>? ownServers = null)
    {
        pluginServers ??= NoPluginServers;
        ownServers ??= [];
        var problems = new List<string>();
        if (!StudioNames.IsValidName(draft.Name))
            problems.Add($"Name: lowercase letters, digits and '-', at most {StudioNames.MaxNameLength} characters, starting with a letter and ending with a letter or digit.");
        else if (StudioNames.IsReserved(draft.Name))
            problems.Add($"Name '{draft.Name}' is reserved (the seed and the studio's own agents).");
        if (draft.Description.Length > MaxDescription || HasControl(draft.Description, allowNewlines: false))
            problems.Add($"Description: one line, at most {MaxDescription} characters, no control or invisible formatting characters.");
        if (!StudioNames.IsKubeName(draft.ModelConfig))
            problems.Add("Model: pick one of the configured models.");
        if (draft.Instructions.Trim().Length == 0 || draft.Instructions.Length > MaxInstructions || HasControl(draft.Instructions, allowNewlines: true))
            problems.Add($"Instructions: required, at most {MaxInstructions} characters, no control or invisible formatting characters.");

        if (draft.Skills.Count > MaxSkills || draft.Skills.Any(s => !SkillRef().IsMatch(s)) || draft.Skills.Distinct().Count() != draft.Skills.Count)
            problems.Add($"Skills: at most {MaxSkills} distinct OCI image references pinned by digest (registry/repository[:tag]@sha256:…).");
        // D122: a skill runs code in the agent's pod; only registries the owner allow-listed, and a digest, so what a
        // supervisor reviewed is what runs.
        if (draft.Skills.Count > 0 && options.SkillRegistries.Count == 0)
            problems.Add("Skills are off: the owner has not allow-listed a skill registry (Studio:SkillRegistries).");
        else
            foreach (var skill in draft.Skills.Where(s => !options.SkillRegistries.Any(r => r.Length > 0 && s.StartsWith(r.TrimEnd('/') + "/", StringComparison.Ordinal))))
                problems.Add($"Skill '{skill}' is not from an allow-listed registry ({string.Join(", ", options.SkillRegistries)}).");

        var allowed = options.McpServers.Where(s => !s.StartsWith("skanyxx-memory-", StringComparison.Ordinal)).ToHashSet();
        if (draft.McpTools.Count > MaxMcpTools || draft.McpTools.Select(t => t.Server).Distinct().Count() != draft.McpTools.Count)
            problems.Add("MCP tools: each server once.");
        foreach (var choice in draft.McpTools)
        {
            var plugin = pluginServers.ContainsKey(choice.Server) && !allowed.Contains(choice.Server);
            var own = plugin && ownServers.Contains(choice.Server);
            if (!allowed.Contains(choice.Server) && !plugin)
                problems.Add($"MCP server '{choice.Server}' is not on the allow-list ({(allowed.Count + pluginServers.Count == 0 ? "empty" : string.Join(", ", allowed.Concat(pluginServers.Keys).Order()))}).");
            if ((choice.Tools.Count == 0 && !own) || choice.Tools.Any(t => !ToolName().IsMatch(t)) || choice.Tools.Distinct().Count() != choice.Tools.Count)
                problems.Add(own
                    ? $"MCP server '{choice.Server}': tool names each once (none = every tool)."
                    : $"MCP server '{choice.Server}': name at least one tool, each once.");
        }
        var thirdParty = draft.McpTools.Select(t => t.Server)
            .Where(s => pluginServers.ContainsKey(s) && !allowed.Contains(s) && !ownServers.Contains(s)).Distinct().Order(StringComparer.Ordinal).ToList();
        if (thirdParty.Count > 0)
        {
            var named = string.Join(", ", thirdParty.Select(s => $"'{s}'"));
            if (draft.Grants.Count > 0 || draft.MemoryTtlDays is not null)
                problems.Add($"MCP server {named} is a plugin's (third-party) server: an agent with memory grants or kagent TTL memory never uses one, "
                    + "so nothing a third party says can reach company memory.");
            if (thirdParty.Select(s => pluginServers[s]).Distinct().Count() > 1)
                problems.Add($"MCP servers {named} belong to different plugins: an agent uses third-party servers of one plugin at most, so one party's "
                    + "content never steers another's credential.");
            if (draft.McpTools.Select(t => t.Server).Where(allowed.Contains).Order(StringComparer.Ordinal).ToList() is { Count: > 0 } internalServers)
                problems.Add($"MCP server {named} is a plugin's (third-party) server: an agent never uses one beside an allow-listed server "
                    + $"({string.Join(", ", internalServers)}), so a third party's content never steers the owner's tools.");
        }

        if (draft.Grants.Count > MaxGrants || draft.Grants.Select(g => g.Scope).Distinct().Count() != draft.Grants.Count)
            problems.Add($"Memory grants: each scope once, at most {MaxGrants}.");
        foreach (var grant in draft.Grants)
        {
            if (!IsSharedScope(grant.Scope))
                problems.Add($"Memory grant '{grant.Scope}': the scope is 'company', 'team:<slug>' or 'department:<slug>' (a studio agent acts for nobody, so no personal scope).");
            if (!grant.Search && !grant.Upsert)
                problems.Add($"Memory grant '{grant.Scope}': give search, upsert or both; leave the scope out for neither.");
        }

        if (draft.MemoryTtlDays is { } ttl && (ttl < 1 || ttl > MaxTtlDays))
            problems.Add($"kagent TTL memory: 1 to {MaxTtlDays} days, or off.");
        return problems;
    }

    /// <summary>D091: a team or department grant is the owner's to give (or take away).</summary>
    public static bool OpensTeamOrDepartment(IEnumerable<StudioGrant> grants) =>
        grants.Any(g => (g.Search || g.Upsert) && (g.Scope.StartsWith("team:", StringComparison.Ordinal) || g.Scope.StartsWith("department:", StringComparison.Ordinal)));

    private static bool IsSharedScope(string scope) =>
        scope == "company"
        || (scope.StartsWith("team:", StringComparison.Ordinal) && Identifier.IsValid(scope["team:".Length..]))
        || (scope.StartsWith("department:", StringComparison.Ordinal) && Identifier.IsValid(scope["department:".Length..]));

    /// <summary>
    /// Control characters, and Unicode format characters (bidi overrides, zero-width): those make the review page show
    /// a supervisor something other than what the agent is told (I4, as for display names).
    /// </summary>
    internal static bool HasControl(string text, bool allowNewlines) =>
        text.Any(c => (char.IsControl(c) && !(allowNewlines && c is '\n' or '\t')) || char.GetUnicodeCategory(c) == UnicodeCategory.Format);

    // registry/path[:tag]@sha256:digest — no scheme, no whitespace, always a digest.
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9.-]*[a-z0-9])?(:[0-9]+)?(/[a-z0-9]([a-z0-9._-]*[a-z0-9])?)+(:[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})?@sha256:[a-f0-9]{64}\z")]
    internal static partial Regex SkillRef();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z")]
    internal static partial Regex ToolName();
}
