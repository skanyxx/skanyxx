using System.Text.RegularExpressions;
using Skanyxx.Module.Agents.Studio;
using Skanyxx.Module.Agents.Studio.Definition;

namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>
/// What a direct agent may hold (D251), in words for the person. The studio's limits (<see cref="DraftRules"/>) where
/// they overlap: allow-listed MCP servers with named tools, digest-pinned skills from allow-listed registries. What the
/// agent already has may stay as it is (an agent made in kagent-ui is still editable), never be widened.
/// </summary>
internal static partial class AgentFormRules
{
    public const int MaxNameLength = 63;
    public const int MaxTools = 20;
    public const int MaxA2aSkills = 20;
    public const int MaxA2aSkillName = 100;
    public const int MaxA2aText = 500;
    public const int MaxA2aItems = 20;

    /// <param name="existing">The agent's current spec on an update; null on a create.</param>
    /// <param name="models">ModelConfig names in the agents' namespace.</param>
    /// <param name="callable">Agents another agent may call as a tool (<see cref="AgentProtection.IsCallable"/>).</param>
    public static IReadOnlyList<string> Check(AgentForm form, StudioOptions options, ExistingSpec? existing,
        IReadOnlyCollection<string> models, IReadOnlyCollection<string> callable)
    {
        var problems = new List<string>();
        if (form.McpTools is null || form.AgentTools is null || form.Skills is null || form.A2aSkills is null
            || form.Description is null || form.SystemMessage is null || form.ModelConfig is null)
            return ["Lists and texts must not be null."];

        if (form.Namespace != options.Namespace)
            problems.Add($"Namespace: agents are managed in '{options.Namespace}' only.");
        if (existing is null)
        {
            if (!IsAgentName(form.Name))
                problems.Add($"Name: lowercase letters, digits and '-', at most {MaxNameLength} characters, starting with a letter and ending with a letter or digit.");
            else if (StudioNames.IsReserved(form.Name))
                problems.Add($"Name '{form.Name}' is reserved for Skanyxx's own agents (seed, skanyxx-*, preview-*).");
        }
        if (form.Description.Length > DraftRules.MaxDescription || DraftRules.HasControl(form.Description, allowNewlines: false))
            problems.Add($"Description: one line, at most {DraftRules.MaxDescription} characters, no control or invisible formatting characters.");
        if (form.SystemMessage.Trim().Length == 0 || form.SystemMessage.Length > DraftRules.MaxInstructions
            || DraftRules.HasControl(form.SystemMessage, allowNewlines: true))
            problems.Add($"System prompt: required, at most {DraftRules.MaxInstructions} characters, no control or invisible formatting characters.");
        if (!models.Contains(form.ModelConfig))
            problems.Add("Model: pick one of the configured models.");
        if (form.Runtime is not (null or "" or "go" or "python"))
            problems.Add("Runtime: 'go', 'python' or empty for kagent's default.");

        CheckMcpTools(form, options, existing, problems);
        CheckAgentTools(form, existing, callable, problems);
        CheckSkills(form, options, existing, problems);
        CheckA2a(form, problems);
        return problems;
    }

    public static bool IsAgentName(string? name) => name is not null && name.Length <= MaxNameLength && AgentName().IsMatch(name);

    private static void CheckMcpTools(AgentForm form, StudioOptions options, ExistingSpec? existing, List<string> problems)
    {
        if (form.McpTools.Count + form.AgentTools.Count > MaxTools)
            problems.Add($"Tools: at most {MaxTools} MCP servers and agents together.");
        if (form.McpTools.Any(t => t?.Server is null || t.Tools is null))
        {
            problems.Add("MCP tools: each needs a server and a tool list.");
            return;
        }
        if (form.McpTools.Select(t => t.Server).Distinct().Count() != form.McpTools.Count)
            problems.Add("MCP tools: each server once.");
        var allowed = options.McpServers.Where(s => !s.StartsWith(StudioNames.MemoryServerPrefix, StringComparison.Ordinal)).ToHashSet();
        foreach (var choice in form.McpTools)
        {
            var kept = existing?.McpTools.GetValueOrDefault(choice.Server) is { } before && before.SetEquals(choice.Tools);
            if (!allowed.Contains(choice.Server) && !kept)
                problems.Add($"MCP server '{choice.Server}' is not on the allow-list ({(allowed.Count == 0 ? "empty" : string.Join(", ", allowed.Order()))}).");
            if (choice.Tools.Count == 0 && !kept)
                problems.Add($"MCP server '{choice.Server}': name at least one tool.");
            if (choice.Tools.Count > DraftRules.MaxMcpTools || choice.Tools.Any(t => t is null || !DraftRules.ToolName().IsMatch(t))
                || choice.Tools.Distinct().Count() != choice.Tools.Count)
                problems.Add($"MCP server '{choice.Server}': at most {DraftRules.MaxMcpTools} tool names, each once.");
        }
    }

    private static void CheckAgentTools(AgentForm form, ExistingSpec? existing, IReadOnlyCollection<string> callable, List<string> problems)
    {
        if (form.AgentTools.Distinct().Count() != form.AgentTools.Count)
            problems.Add("Agent tools: each agent once.");
        foreach (var agent in form.AgentTools)
            if (agent is null || agent == form.Name || !(callable.Contains(agent) || existing?.AgentTools.Contains(agent) == true))
                problems.Add($"Agent tool '{agent}': name another agent in '{form.Namespace}' made on the Agents page or outside Skanyxx "
                    + "(never the seed, a studio agent or one of Skanyxx's own).");
    }

    private static void CheckSkills(AgentForm form, StudioOptions options, ExistingSpec? existing, List<string> problems)
    {
        if (form.Skills.Count > DraftRules.MaxSkills || form.Skills.Distinct().Count() != form.Skills.Count)
            problems.Add($"Skills: at most {DraftRules.MaxSkills}, each once.");
        // D122: a new skill runs code in the agent's pod, so only a digest from a registry the owner allow-listed.
        foreach (var skill in form.Skills.Where(s => existing?.Skills.Contains(s) != true))
            if (skill is null || !DraftRules.SkillRef().IsMatch(skill))
                problems.Add($"Skill '{skill}': an OCI image pinned by digest (registry/repository[:tag]@sha256:…).");
            else if (!options.SkillRegistries.Any(r => r.Length > 0 && skill.StartsWith(r.TrimEnd('/') + "/", StringComparison.Ordinal)))
                problems.Add(options.SkillRegistries.Count == 0
                    ? "Skills are off: the owner has not allow-listed a skill registry (Studio:SkillRegistries)."
                    : $"Skill '{skill}' is not from an allow-listed registry ({string.Join(", ", options.SkillRegistries)}).");
    }

    private static void CheckA2a(AgentForm form, List<string> problems)
    {
        if (form.A2aSkills.Count > MaxA2aSkills)
            problems.Add($"A2A skills: at most {MaxA2aSkills}.");
        if (form.A2aSkills.Any(s => s is null))
        {
            problems.Add("A2A skills: no empty entries.");
            return;
        }
        if (form.A2aSkills.Select(s => s.Id).Distinct().Count() != form.A2aSkills.Count)
            problems.Add("A2A skills: each id once.");
        foreach (var skill in form.A2aSkills)
        {
            if (skill.Id is null || !A2aId().IsMatch(skill.Id))
                problems.Add("A2A skill id: letters, digits, '.', '_' or '-', at most 64.");
            if (string.IsNullOrWhiteSpace(skill.Name) || !OneLine(skill.Name, MaxA2aSkillName))
                problems.Add($"A2A skill '{skill.Id}': a name of one line, at most {MaxA2aSkillName} characters.");
            if (skill.Description is not null && !OneLine(skill.Description, MaxA2aText))
                problems.Add($"A2A skill '{skill.Id}': a description of one line, at most {MaxA2aText} characters.");
            if (skill.Tags is { } tags && (tags.Count > MaxA2aItems || tags.Any(t => t is null || !OneLine(t, MaxA2aSkillName))))
                problems.Add($"A2A skill '{skill.Id}': at most {MaxA2aItems} tags of one line, at most {MaxA2aSkillName} characters each.");
            if (skill.Examples is { } examples && (examples.Count > MaxA2aItems || examples.Any(e => e is null || !OneLine(e, MaxA2aText))))
                problems.Add($"A2A skill '{skill.Id}': at most {MaxA2aItems} examples of one line, at most {MaxA2aText} characters each.");
        }
    }

    private static bool OneLine(string text, int max) => text.Length <= max && !DraftRules.HasControl(text, allowNewlines: false);

    // kagent derives a Service from the name: a DNS-1035 label.
    [GeneratedRegex("^[a-z]([-a-z0-9]*[a-z0-9])?\\z")]
    private static partial Regex AgentName();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z")]
    private static partial Regex A2aId();
}
