namespace Skanyxx.Module.Investigate.Agents;

/// <summary>An agent a step may call, as the editor's dropdown lists it. <paramref name="Ref"/> is <c>namespace/name</c>.</summary>
public sealed record AgentChoice(string Ref, string Namespace, string Name, string Description);
