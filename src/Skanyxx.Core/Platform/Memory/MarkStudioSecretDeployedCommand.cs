using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Records that kagent now holds the studio secret with <paramref name="Fingerprint"/> (D118). A pass that finds memory's
/// live secret and this mark differ issues again: a failure between issue and create never leaves the two apart for good.
/// </summary>
public sealed record MarkStudioSecretDeployedCommand(string Actor, string AgentId, string Fingerprint) : IRequest<Outcome<bool>>;
