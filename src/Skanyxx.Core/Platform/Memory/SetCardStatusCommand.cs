using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// A person publishes a candidate, or marks a card stale — superseded by <paramref name="SupersededBy"/>
/// (<c>scope/key</c>) when given — or brings a stale card back (D011). Checked against <paramref name="Version"/> (D041).
/// </summary>
public sealed record SetCardStatusCommand(
    LibraryUser User, string Scope, string Key, int Version, string Status, string? SupersededBy) : IRequest<Outcome<CardDto>>;
