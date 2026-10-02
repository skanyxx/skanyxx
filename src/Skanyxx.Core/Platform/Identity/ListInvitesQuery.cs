using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>Invites not yet accepted, revoked or expired. Never their tokens.</summary>
public sealed record ListInvitesQuery : IRequest<Outcome<IReadOnlyList<PendingInvite>>>;
