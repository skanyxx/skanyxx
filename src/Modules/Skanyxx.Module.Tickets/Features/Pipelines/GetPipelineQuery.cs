using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

public sealed record GetPipelineQuery(string? UserId, string Id) : IRequest<Outcome<Pipeline>>;
