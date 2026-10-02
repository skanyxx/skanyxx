using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Starts a Microsoft sign-in that comes back to <paramref name="CompletionPath"/> (a local path). With
/// <paramref name="LinkUserId"/>, it is a link for that signed-in account: <paramref name="LinkPassword"/> must be the
/// account's current password (D10), the owner cannot link (D11), and the result is bound to that user id, so only
/// <see cref="LinkEntraCommand"/> for the same user accepts it. <c>404</c> when Microsoft sign-in is off. The password
/// is printed as <c>***</c>.
/// </summary>
public sealed record EntraChallengeQuery(string CompletionPath, string? LinkUserId = null, string? LinkPassword = null)
    : IRequest<Outcome<EntraChallenge>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"CompletionPath = {CompletionPath}, LinkUserId = {LinkUserId}, LinkPassword = {(LinkPassword is null ? "null" : "***")}");
        return true;
    }
}
