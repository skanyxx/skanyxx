using MediatR;
using Microsoft.AspNetCore.Identity;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.SignOut;

internal sealed class SignOutHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, RefreshChains chains)
    : IRequestHandler<SignOutCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(SignOutCommand command, CancellationToken ct)
    {
        if (await users.FindByIdAsync(command.UserId) is { } user)
        {
            await RotateStampAsync(user, ct);
            await chains.RevokeAllAsync(user.Id, ct);
        }

        await signIn.SignOutAsync();
        return Outcome<bool>.Ok(true);
    }

    /// <summary>
    /// A concurrent write to the user row (a failed sign-in, another sign-out) makes the update a concurrency failure;
    /// retry once on fresh values. Still failing means nothing was revoked, and the caller must not be told otherwise.
    /// </summary>
    private async Task RotateStampAsync(IdentityUser user, CancellationToken ct)
    {
        if ((await users.UpdateSecurityStampAsync(user)).Succeeded)
            return;

        await db.Entry(user).ReloadAsync(ct);
        var retry = await users.UpdateSecurityStampAsync(user);
        if (!retry.Succeeded)
            throw new InvalidOperationException(
                $"Sign-out could not rotate the security stamp of user {user.Id}: {string.Join(", ", retry.Errors.Select(e => e.Code))}.");
    }
}
