using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>The versions a card replaced, for whoever may open the card.</summary>
public sealed record CardHistoryQuery(LibraryUser User, string Scope, string Key) : IRequest<Outcome<IReadOnlyList<CardVersionDto>>>;
