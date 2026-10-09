using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Published by identity after it commits a change that leaves <paramref name="UserId"/> without supervisor privileges:
/// roles saved without supervisor (unchanged ones too, so saving them again re-drives a revocation that failed), the
/// account disabled, or the person refused by the Entra group re-check. Modules holding durable credentials that person
/// issued revoke them here (memory: the agent secrets they created), since ending the person's sessions does not reach
/// those. <paramref name="AccountDisabled"/>: the account is disabled at the moment of publishing (read then, so a
/// retried revocation carries it too). <paramref name="AccessRemoved"/>: the person can no longer use Skanyxx at all —
/// disabled, or refused by the Entra re-check (no mapped group left, or gone from the tenant; D161) — also read at
/// publishing; modules that run things on the person's behalf stop them (sandboxes: their active AX tasks, D154).
/// Every handler runs even when another fails (<see cref="AllHandlersPublisher"/>). Handlers must be idempotent.
/// </summary>
public sealed record PrivilegesRevoked(string UserId, string Reason, bool AccountDisabled = false, bool AccessRemoved = false) : INotification;
