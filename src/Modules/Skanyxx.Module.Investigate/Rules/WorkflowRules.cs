using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Rules;

/// <summary>
/// What makes a step tree runnable: within the depth and size caps, unique ids, every agent step with an agent and a
/// prompt, every group with steps, and every <c>{{steps.x}}</c> naming a step that has finished by the time it runs (an
/// earlier step of a sequence, never a sibling in a parallel group or a step after it). Messages are for people.
/// </summary>
public static class WorkflowRules
{
    public const int MaxTitleChars = 120;

    public static IReadOnlyList<string> Validate(IReadOnlyList<WorkflowStep>? steps, int maxDepth, int maxSteps, int maxPromptChars)
    {
        if (steps is null || steps.Count == 0)
            return ["Add at least one step."];

        if (Malformed(steps))
            return ["Every step needs its fields, and a list of steps (empty for an agent step)."];

        var errors = new List<string>();
        var nodes = WorkflowTree.Flatten(steps).ToList();
        if (nodes.Count > maxSteps)
            return [$"A workflow has at most {maxSteps} steps (groups count); this one has {nodes.Count}."];
        if (nodes.Max(n => n.Depth) > maxDepth)
            errors.Add($"Steps nest at most {maxDepth} levels deep.");

        foreach (var duplicate in nodes.GroupBy(n => n.Step.Id).Where(g => g.Count() > 1).Select(g => g.Key))
            errors.Add($"Two steps share the id '{duplicate}'.");

        foreach (var (step, _) in nodes)
            errors.AddRange(StepErrors(step, maxPromptChars));

        if (errors.Count == 0)
            Walk(steps, GroupMode.Sequential, [], [.. nodes.Select(n => n.Step.Id)], errors);
        return errors;
    }

    private static IEnumerable<string> StepErrors(WorkflowStep step, int maxPromptChars)
    {
        var name = string.IsNullOrWhiteSpace(step.Title) ? $"Step '{step.Id}'" : $"Step '{step.Title}'";
        if (!StepId.IsValid(step.Id))
            yield return $"{name}: its id must be 1-32 lowercase letters, digits or '-'.";
        if (string.IsNullOrWhiteSpace(step.Title) || step.Title.Length > MaxTitleChars)
            yield return $"{name}: give it a title of at most {MaxTitleChars} characters.";
        if (HasNul(step.Title) || HasNul(step.Prompt))
            yield return $"{name}: must not contain NUL characters.";

        if (step.Kind == StepKind.Group)
        {
            if (step.Steps.Count == 0)
                yield return $"{name}: a group needs at least one step.";
            if (step.Agent is not null || step.Prompt is not null)
                yield return $"{name}: a group has no agent or prompt of its own.";
            yield break;
        }

        if (step.Steps.Count > 0)
            yield return $"{name}: an agent step cannot hold steps; make it a group.";
        if (!AgentRef.IsValid(step.Agent))
            yield return $"{name}: pick an agent.";
        if (string.IsNullOrWhiteSpace(step.Prompt))
            yield return $"{name}: write what the agent should do.";
        else if (step.Prompt.Length > maxPromptChars)
            yield return $"{name}: the prompt is longer than {maxPromptChars} characters.";
    }

    // `done` holds the ids that have finished before the steps at this level start.
    private static void Walk(IReadOnlyList<WorkflowStep> steps, GroupMode mode, HashSet<string> done, HashSet<string> all, List<string> errors)
    {
        var after = new HashSet<string>(done);
        foreach (var step in steps)
        {
            var available = mode == GroupMode.Sequential ? after : done;
            if (step.Kind == StepKind.Group)
                Walk(step.Steps, step.Mode, [.. available], all, errors);
            else
                foreach (var reference in PromptTemplate.References(step.Prompt!).Where(r => !available.Contains(r)))
                    errors.Add($"Step '{step.Title}' uses {{{{steps.{reference}}}}}, but that step "
                        + (all.Contains(reference) ? "has not finished by then." : "does not exist."));
            after.UnionWith(WorkflowTree.Flatten([step]).Select(n => n.Step.Id));
        }
    }

    // JSON may carry a null step or a null list; the request is the boundary, so it is refused here.
    private static bool Malformed(IReadOnlyList<WorkflowStep?>? steps) =>
        steps is null || steps.Any(s => s is null || s.Id is null || s.Title is null || Malformed(s.Steps));

    private static bool HasNul(string? text) => text?.Contains('\0') == true;
}
