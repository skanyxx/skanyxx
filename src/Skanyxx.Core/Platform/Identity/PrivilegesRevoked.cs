using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Published by identity after it commits a change that leaves <paramref name="UserId"/> without supervisor privileges:
/// roles saved without supervisor (unchanged ones too, so saving them again re-drives a revocation that failed), or
/// the account disabled. Modules holding durable credentials that person issued revoke them here (memory: the agent
/// secrets they created), since ending the person's sessions does not reach those. Handlers must be idempotent.
/// </summary>
public sealed record PrivilegesRevoked(string UserId, string Reason) : INotification;
