using MediatR;
using Skanyxx.Module.Memory.Contracts;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

public sealed record SearchCardsQuery(MemoryCaller Caller, string Query) : IRequest<IReadOnlyList<CardHit>>;
