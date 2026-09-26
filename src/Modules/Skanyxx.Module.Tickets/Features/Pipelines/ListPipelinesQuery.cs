using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

public sealed record ListPipelinesQuery(string? UserId) : IRequest<Outcome<IReadOnlyList<Pipeline>>>;
