using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// The reconciler created <paramref name="Server"/> in kagent with the spec hashing to <paramref name="SpecHash"/> and the
/// header values with <paramref name="Fingerprint"/>: from now on it is the studio's to replace or delete (claimed).
/// </summary>
public sealed record MarkPluginServerDeployedCommand(string Actor, string Server, string Plugin, string Fingerprint, string SpecHash) : IRequest<Outcome<bool>>;
