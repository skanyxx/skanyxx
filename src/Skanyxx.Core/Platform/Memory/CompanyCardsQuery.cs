using MediatR;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Published <c>company</c> cards matching <paramref name="Text"/> (any word, best first), at most <paramref name="Limit"/>.
/// Needs no person: every signed-in person may read them (D048). The chat index's related work (D238).
/// </summary>
public sealed record CompanyCardsQuery(string Text, int Limit) : IRequest<IReadOnlyList<LibraryEntry>>
{
    public const int MaxLimit = 20;
}
