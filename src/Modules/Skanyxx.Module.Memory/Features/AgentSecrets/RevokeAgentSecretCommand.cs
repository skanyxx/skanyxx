using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

/// <summary>Deletes the agent's secret; the agent is locked out of <c>/mcp/memory</c> until a new one is issued.</summary>
public sealed record RevokeAgentSecretCommand(MemoryCaller Caller, string AgentId) : IRequest<Outcome<string>>;
