using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

public sealed record GetTaskQuery(string? UserId, string Name) : IRequest<Outcome<SandboxTask>>;
