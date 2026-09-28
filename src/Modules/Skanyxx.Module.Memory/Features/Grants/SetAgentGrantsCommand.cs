using Skanyxx.Core.Platform;
using MediatR;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Grants;

/// <summary>Replaces every grant the agent has.</summary>
public sealed record SetAgentGrantsCommand(MemoryCaller Caller, string AgentId, IReadOnlyList<GrantEntry> Grants)
    : IRequest<Outcome<IReadOnlyList<GrantEntry>>>;
