using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// D038: a person renames a card's key within its scope, where they may write, if the card is still at
/// <paramref name="Version"/>. Cards lifted from it keep their link (it is by id). A human action: agents propose keys.
/// </summary>
public sealed record RenameCardCommand(LibraryUser User, string Scope, string Key, string NewKey, int Version)
    : IRequest<Outcome<CardDto>>
{
    /// <summary>Conflict reason: the card moved on from <see cref="Version"/>; the body carries it as it is now.</summary>
    public const string Stale = "stale";

    /// <summary>Conflict reason: <see cref="NewKey"/> is in use in the scope; the body carries the card already there.</summary>
    public const string Taken = "taken";
}
