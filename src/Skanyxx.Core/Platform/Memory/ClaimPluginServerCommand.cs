using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Recorded before the reconciler creates <paramref name="Server"/> in kagent, so a failure between the create and
/// <see cref="MarkPluginServerDeployedCommand"/> never leaves a server the studio made looking like someone else's.
/// Refused when another plugin's server by that name is claimed.
/// </summary>
public sealed record ClaimPluginServerCommand(string Actor, string Server, string Plugin) : IRequest<Outcome<bool>>;
