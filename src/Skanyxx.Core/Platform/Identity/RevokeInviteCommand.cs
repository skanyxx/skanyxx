using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>Not found once the invite is accepted, revoked or expired.</summary>
public sealed record RevokeInviteCommand(string ActorId, string InviteId) : IRequest<Outcome<bool>>;
