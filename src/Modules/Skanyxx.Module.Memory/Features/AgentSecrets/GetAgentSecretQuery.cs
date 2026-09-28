using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

/// <summary>Whether the agent has a secret and since when; never the secret itself.</summary>
public sealed record GetAgentSecretQuery(MemoryCaller Caller, string AgentId) : IRequest<Outcome<AgentSecretStatus>>;
