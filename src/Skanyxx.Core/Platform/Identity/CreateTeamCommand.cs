using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>The slug is the team's id for good (memory scope <c>team:&lt;slug&gt;</c>); the name and department can change.</summary>
public sealed record CreateTeamCommand(string ActorId, string Slug, string Name, string Department) : IRequest<Outcome<TeamDto>>;
