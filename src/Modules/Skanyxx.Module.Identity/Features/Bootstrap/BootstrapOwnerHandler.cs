using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.Bootstrap;

/// <summary>
/// Check-then-create under a transaction-scoped advisory lock: two concurrent bootstraps serialize, and the second
/// sees the first owner and gets a conflict. Replicas share the lock through the database.
/// </summary>
internal sealed class BootstrapOwnerHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, BootstrapGuard guard, AccountReader accounts)
    : IRequestHandler<BootstrapOwnerCommand, Outcome<AccountDto>>
{
    private const long BootstrapLockKey = 0x49444E01;

    public async Task<Outcome<AccountDto>> Handle(BootstrapOwnerCommand command, CancellationToken ct)
    {
        if (!guard.AllowsBootstrap(command.BootstrapToken, command.FromLoopback))
            return Outcome<AccountDto>.Forbidden(guard.HasToken
                ? "Setup needs the bootstrap token."
                : "Setup needs the bootstrap token: set Identity:BootstrapToken (loopback setup without it is for Development only).");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({BootstrapLockKey})", ct);
        if (await db.Users.AnyAsync(ct))
            return Outcome<AccountDto>.Conflict(null, "Setup is already complete; sign in instead.");

        var user = new IdentityUser { UserName = command.Email, Email = command.Email };
        Require(await users.CreateAsync(user, command.Password));
        Require(await users.AddToRoleAsync(user, SkanyxxRoles.Owner));
        if (!string.IsNullOrWhiteSpace(command.DisplayName))
            Require(await users.AddClaimAsync(user, AccountReader.DisplayNameClaim(command.DisplayName.Trim())));
        await transaction.CommitAsync(ct);

        return Outcome<AccountDto>.Created(await accounts.ToDtoAsync(user));
    }

    // Identity's own password/email rules report here; they become the usual 400 keyed by field.
    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new ValidationException(result.Errors.Select(e =>
                new ValidationFailure(e.Code.StartsWith("Password") ? nameof(BootstrapOwnerCommand.Password) : nameof(BootstrapOwnerCommand.Email), e.Description)));
    }
}
