using MediatR;

namespace Skanyxx.Core.Platform.Identity;

public sealed record ListTeamsQuery : IRequest<Outcome<IReadOnlyList<TeamDto>>>;
