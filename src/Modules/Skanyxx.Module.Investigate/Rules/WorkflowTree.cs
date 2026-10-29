using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Rules;

internal static class WorkflowTree
{
    /// <summary>Every step, depth first, in the order the editor shows them.</summary>
    public static IEnumerable<TreeNode> Flatten(IEnumerable<WorkflowStep> steps, int depth = 1)
    {
        foreach (var step in steps)
        {
            yield return new TreeNode(step, depth);
            foreach (var child in Flatten(step.Steps, depth + 1))
                yield return child;
        }
    }

    /// <summary>The kagent agents (<c>namespace/name</c>) a tree calls, each once.</summary>
    public static IReadOnlyList<string> Agents(IEnumerable<WorkflowStep> steps) =>
        [.. Flatten(steps).Where(n => n.Step.Kind == StepKind.Agent && n.Step.Agent is not null).Select(n => n.Step.Agent!).Distinct()];
}
