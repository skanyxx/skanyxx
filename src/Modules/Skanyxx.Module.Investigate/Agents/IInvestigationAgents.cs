namespace Skanyxx.Module.Investigate.Agents;

/// <summary>The agents a workflow may use. Throws when kagent cannot be asked; that is never "no agents".</summary>
public interface IInvestigationAgents
{
    Task<IReadOnlyList<AgentChoice>> ListAsync(CancellationToken ct);
}
