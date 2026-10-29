using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// A person deletes one of their own personal cards, or the owner a shared card (D507), checked against
/// <paramref name="Version"/> (D041). The value is the card as it was.
/// </summary>
public sealed record DeleteCardCommand(LibraryUser User, string Scope, string Key, int Version) : IRequest<Outcome<CardDto>>;
