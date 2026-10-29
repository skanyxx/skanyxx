using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Deletes a team (D351). With members it is refused (409) unless <paramref name="ReassignTo"/> names another team,
/// which then receives them. The slug is retired, never reused. The result is the number of members moved.
/// </summary>
public sealed record DeleteTeamCommand(string ActorId, string Slug, string? ReassignTo) : IRequest<Outcome<int>>;
