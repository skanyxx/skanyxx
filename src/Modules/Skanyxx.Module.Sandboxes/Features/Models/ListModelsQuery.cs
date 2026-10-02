using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Models;

public sealed record ListModelsQuery(string? UserId) : IRequest<Outcome<IReadOnlyList<SandboxModel>>>;
