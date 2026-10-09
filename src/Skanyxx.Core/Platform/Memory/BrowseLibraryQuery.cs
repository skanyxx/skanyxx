using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// The library list: cards in the scopes this person may read, optionally in one <paramref name="Scope"/> and matching
/// <paramref name="Text"/> (full-text, any word). <c>company</c> lists published cards only (D048); the person's
/// personal, team and department scopes list every status.
/// </summary>
public sealed record BrowseLibraryQuery(LibraryUser User, string? Text, string? Scope) : IRequest<Outcome<LibraryShelf>>;
