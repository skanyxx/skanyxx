using MediatR;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Features.Unlock;

/// <summary>
/// Break-glass: clears the lockout of the owner with <paramref name="Email"/>, for whoever holds the bootstrap token.
/// Lockout counts failures from anyone, so without this a stranger who knows the owner's email could keep the only
/// account locked. Not available when no bootstrap token is configured.
/// </summary>
public sealed record UnlockOwnerCommand(string Email, string? BootstrapToken) : IRequest<Outcome<bool>>;
