using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.AgentSecrets;

/// <summary>
/// Issues the agent's secret, replacing (and so revoking) any previous one. <paramref name="ActsForUsers"/> is set by
/// every issue, rotation included: a rotation that leaves it out turns it off (D084).
/// </summary>
public sealed record IssueAgentSecretCommand(MemoryCaller Caller, string AgentId, bool ActsForUsers)
    : IRequest<Outcome<IssuedAgentSecret>>;
