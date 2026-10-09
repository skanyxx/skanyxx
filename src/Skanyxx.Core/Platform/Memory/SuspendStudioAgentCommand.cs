using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// The owner's emergency stop for a studio agent (D121). Suspending revokes its secret and grants at once and keeps them
/// off: the reconciler removes the agent from kagent and issues nothing until it is resumed. Only for a studio agent.
/// </summary>
public sealed record SuspendStudioAgentCommand(string Actor, string AgentId, bool Suspended) : IRequest<Outcome<bool>>;
