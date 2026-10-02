using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>Renames the team and/or moves it to another department; its members' department membership follows at once.</summary>
public sealed record UpdateTeamCommand(string ActorId, string Slug, string Name, string Department) : IRequest<Outcome<TeamDto>>;
