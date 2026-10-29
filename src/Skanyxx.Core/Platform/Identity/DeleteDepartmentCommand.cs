using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Deletes a department (D351). With teams it is refused (409) unless <paramref name="MoveTeamsTo"/> names another
/// department, which then receives them with their members. The slug is retired. The result is the number of teams moved.
/// </summary>
public sealed record DeleteDepartmentCommand(string ActorId, string Slug, string? MoveTeamsTo) : IRequest<Outcome<int>>;
