using MediatR;
using Microsoft.AspNetCore.Identity;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Unlock;

internal sealed class UnlockOwnerHandler(AccountsDbContext db, UserManager<IdentityUser> users, BootstrapGuard guard)
    : IRequestHandler<UnlockOwnerCommand, Outcome<bool>>
{
    public async Task<Outcome<bool>> Handle(UnlockOwnerCommand command, CancellationToken ct)
    {
        if (!guard.HasToken)
            return Outcome<bool>.NotFound("Unlock is only available when Identity:BootstrapToken is set.");
        if (!guard.Matches(command.BootstrapToken))
            return Outcome<bool>.Unauthorized("The bootstrap token is missing or wrong.");

        // Same lock as sign-in, so a guess in flight cannot write a stale failure count over the reset.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLock.AcquireAsync(db, users.NormalizeEmail(command.Email), ct);
        if (await users.FindByEmailAsync(command.Email) is not { } user || !await users.IsInRoleAsync(user, SkanyxxRoles.Owner))
            return Outcome<bool>.NotFound("No owner has that email.");

        Require(await users.SetLockoutEndDateAsync(user, null));
        Require(await users.ResetAccessFailedCountAsync(user));
        await transaction.CommitAsync(ct);
        return Outcome<bool>.Ok(true);
    }

    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Unlock failed: {string.Join(", ", result.Errors.Select(e => e.Code))}.");
    }
}
