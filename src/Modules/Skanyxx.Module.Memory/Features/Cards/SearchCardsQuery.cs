using MediatR;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

public sealed record SearchCardsQuery(Caller Caller, string Query) : IRequest<IReadOnlyList<CardHit>>;
