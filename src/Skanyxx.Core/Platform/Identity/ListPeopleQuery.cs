using MediatR;

namespace Skanyxx.Core.Platform.Identity;

public sealed record ListPeopleQuery : IRequest<Outcome<IReadOnlyList<PersonDto>>>;
