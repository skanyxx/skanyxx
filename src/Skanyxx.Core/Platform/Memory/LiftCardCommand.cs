using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>Copies a published card up to a higher scope; the original stays (D051). A human action.</summary>
public sealed record LiftCardCommand(LibraryUser User, string FromScope, string Key, string ToScope) : IRequest<Outcome<CardDto>>;
