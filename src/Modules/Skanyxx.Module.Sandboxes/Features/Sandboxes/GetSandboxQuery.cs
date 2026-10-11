using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

public sealed record GetSandboxQuery(string? UserId, string Name) : IRequest<Outcome<Sandbox>>;
