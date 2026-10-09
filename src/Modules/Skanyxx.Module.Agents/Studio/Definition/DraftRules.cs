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

    public static IReadOnlyList<string> Check(AgentDraft draft, StudioOptions options)
    {
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
            if (!allowed.Contains(choice.Server))
                problems.Add($"MCP server '{choice.Server}' is not on the allow-list ({(allowed.Count == 0 ? "empty" : string.Join(", ", allowed))}).");
            if (choice.Tools.Count == 0 || choice.Tools.Any(t => !ToolName().IsMatch(t)) || choice.Tools.Distinct().Count() != choice.Tools.Count)
                problems.Add($"MCP server '{choice.Server}': name at least one tool, each once.");
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
    private static bool HasControl(string text, bool allowNewlines) =>
        text.Any(c => (char.IsControl(c) && !(allowNewlines && c is '\n' or '\t')) || char.GetUnicodeCategory(c) == UnicodeCategory.Format);

    // registry/path[:tag]@sha256:digest — no scheme, no whitespace, always a digest.
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9.-]*[a-z0-9])?(:[0-9]+)?(/[a-z0-9]([a-z0-9._-]*[a-z0-9])?)+(:[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})?@sha256:[a-f0-9]{64}\z")]
    private static partial Regex SkillRef();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z")]
    private static partial Regex ToolName();
}
