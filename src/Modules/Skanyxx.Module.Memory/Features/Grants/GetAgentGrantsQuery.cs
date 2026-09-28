using Skanyxx.Core.Platform;
using MediatR;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Grants;

/// <summary>An empty list means the default: search <c>company</c> + the caller's personal, upsert personal only (D040, D052).</summary>
public sealed record GetAgentGrantsQuery(MemoryCaller Caller, string AgentId) : IRequest<Outcome<IReadOnlyList<GrantEntry>>>;
