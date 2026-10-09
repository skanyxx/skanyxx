using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>What memory holds for a studio agent's name (M5, D117): whose it is, its live studio secret, its grants.</summary>
public sealed record StudioPrincipalQuery(string AgentId) : IRequest<Outcome<StudioPrincipal>>;
