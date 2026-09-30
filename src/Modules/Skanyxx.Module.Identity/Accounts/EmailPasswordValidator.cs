using Microsoft.AspNetCore.Identity;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Refuses a password that contains the account's email local part (case-insensitive, when it is at least
/// <see cref="MinLocalPartLength"/> characters): the first thing a guesser who knows the email tries. Runs wherever
/// Identity checks a password (bootstrap, invite accept, any later password change).
/// </summary>
internal sealed class EmailPasswordValidator : IPasswordValidator<IdentityUser>
{
    public const int MinLocalPartLength = 3;

    public Task<IdentityResult> ValidateAsync(UserManager<IdentityUser> manager, IdentityUser user, string? password)
    {
        var email = user.Email ?? user.UserName ?? "";
        var at = email.IndexOf('@');
        var local = at < 0 ? email : email[..at];
        return Task.FromResult(local.Length >= MinLocalPartLength && password?.Contains(local, StringComparison.OrdinalIgnoreCase) == true
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordContainsEmail",
                Description = "The password must not contain the part of your email before the @."
            })
            : IdentityResult.Success);
    }
}
