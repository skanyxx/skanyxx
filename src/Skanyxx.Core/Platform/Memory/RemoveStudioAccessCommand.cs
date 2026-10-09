using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>Revokes a studio agent's secret and removes its grants (an agent leaving main, a preview ending).</summary>
public sealed record RemoveStudioAccessCommand(string Actor, string AgentId) : IRequest<Outcome<bool>>;
