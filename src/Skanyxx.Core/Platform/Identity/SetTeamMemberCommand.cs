using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>Adds (an existing, enabled person) or removes a team member. Both are idempotent.</summary>
public sealed record SetTeamMemberCommand(string ActorId, string Team, string UserId, bool Member) : IRequest<Outcome<TeamDto>>;
