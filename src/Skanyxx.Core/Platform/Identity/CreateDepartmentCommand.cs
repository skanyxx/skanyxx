using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>The slug is the department's id for good (memory scope <c>department:&lt;slug&gt;</c>); only the name can change.</summary>
public sealed record CreateDepartmentCommand(string ActorId, string Slug, string Name) : IRequest<Outcome<DepartmentDto>>;
