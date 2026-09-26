using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Runs;

/// <summary><see cref="WithPrompts"/> loads each agent turn's full prompt and answer (the dataset needs them).</summary>
public sealed record GetRunQuery(string? UserId, Guid Id, bool WithPrompts = false) : IRequest<Outcome<Run>>;
