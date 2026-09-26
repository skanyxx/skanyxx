using Skanyxx.Core.Platform;
using MediatR;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

public sealed record GetCardQuery(Caller Caller, string Scope, string Key) : IRequest<Outcome<Card>>;
