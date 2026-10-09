using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Published once, after the first owner is created (setup, D025). Handlers do what setup brings with it — the studio
/// creates the agent repo (D023) — and must never fail setup: they log and leave a retry to their own loops.
/// </summary>
public sealed record OwnerBootstrapped(string OwnerId) : INotification;
