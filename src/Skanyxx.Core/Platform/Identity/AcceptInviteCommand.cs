using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Spends the invite (once, under concurrency too), creates the account with the invite's email and roles, and signs
/// it in like <see cref="SignInCommand"/>: a cookie when <paramref name="UseCookie"/>, otherwise bearer + refresh tokens.
/// </summary>
public sealed record AcceptInviteCommand(string Token, string Password, string? DisplayName, bool UseCookie) : IRequest<Outcome<SignedIn>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Token = ***, Password = ***, DisplayName = {DisplayName}, UseCookie = {UseCookie}");
        return true;
    }
}
