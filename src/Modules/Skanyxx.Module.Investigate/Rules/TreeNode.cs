using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Rules;

/// <summary>A step with its place in the tree: 1 for a top-level step.</summary>
public sealed record TreeNode(WorkflowStep Step, int Depth);
