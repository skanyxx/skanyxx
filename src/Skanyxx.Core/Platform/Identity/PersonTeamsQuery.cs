using MediatR;

namespace Skanyxx.Core.Platform.Identity;

public sealed record PersonTeamsQuery(string UserId) : IRequest<Outcome<IReadOnlyList<TeamDto>>>;
