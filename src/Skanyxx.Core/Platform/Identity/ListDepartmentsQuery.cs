using MediatR;

namespace Skanyxx.Core.Platform.Identity;

public sealed record ListDepartmentsQuery : IRequest<Outcome<IReadOnlyList<DepartmentDto>>>;
