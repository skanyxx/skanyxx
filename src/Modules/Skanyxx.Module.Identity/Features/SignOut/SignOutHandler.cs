using MediatR;
using Microsoft.AspNetCore.Identity;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;

namespace Skanyxx.Module.Identity.Features.SignOut;

internal sealed class SignOutHandler(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, SessionRevocation sessions)
    : IRequestHandler<SignOutCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(SignOutCommand command, CancellationToken ct)
    {
        if (await users.FindByIdAsync(command.UserId) is { } user)
            await sessions.EndAllAsync(user, ct);

        await signIn.SignOutAsync();
        return Outcome<bool>.Ok(true);
    }
}
