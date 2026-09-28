using Skanyxx.Core.Platform;
using MediatR;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>Copies a published card up to a higher scope; the original stays (D051).</summary>
public sealed record LiftCardCommand(MemoryCaller Caller, string FromScope, string Key, string ToScope) : IRequest<Outcome<Card>>;
