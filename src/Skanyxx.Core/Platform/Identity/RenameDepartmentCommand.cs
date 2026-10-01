using MediatR;

namespace Skanyxx.Core.Platform.Identity;

public sealed record RenameDepartmentCommand(string ActorId, string Slug, string Name) : IRequest<Outcome<DepartmentDto>>;
