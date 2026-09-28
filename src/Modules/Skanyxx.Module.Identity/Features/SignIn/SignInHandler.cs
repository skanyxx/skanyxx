using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.SignIn;

internal sealed class SignInHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, SignInFailure failure,
    BearerTokens tokens, RefreshChains chains, AccountReader accounts, BootstrapGuard guard, TimeProvider time,
    ILogger<SignInHandler> logger)
    : IRequestHandler<SignInCommand, Outcome<SignedIn>>
{
    /// <summary>
    /// Busy: another sign-in for this email is in flight. Unknown emails take the lock too, so this says nothing about
    /// whether the account exists; it only spares a correct password the "invalid" answer (a double-click, a retry).
    /// </summary>
    public const string BusyMessage = "Sign-in is in progress for this account; try again.";

    public async Task<Outcome<SignedIn>> Handle(SignInCommand command, CancellationToken ct)
    {
        var breakGlass = guard.Matches(command.BootstrapToken);
        var verified = await VerifyAsync(command, breakGlass, ct);
        if (!string.IsNullOrEmpty(command.BootstrapToken))
            logger.LogWarning("Sign-in with a bootstrap token from {Client}: token {Token}, result {Result}.",
                signIn.Context.Connection.RemoteIpAddress, breakGlass ? "valid" : "wrong", verified.Status);
        if (verified.Value is not { } user)
            return new Outcome<SignedIn>(verified.Status, Message: verified.Message);

        var account = await accounts.ToDtoAsync(user);
        var now = time.GetUtcNow();
        if (command.UseCookie)
        {
            await signIn.SignInAsync(user, SessionStart.Stamp(now));
            return Outcome<SignedIn>.Ok(new SignedIn(account, null));
        }

        var chain = await chains.StartAsync(user.Id, now, ct);
        return Outcome<SignedIn>.Ok(new SignedIn(account, tokens.Issue(await signIn.CreateUserPrincipalAsync(user), chain)));
    }

    /// <summary>
    /// The password check, one attempt per account at a time (<see cref="AccountLock"/>), committed whatever the
    /// answer: a failed attempt's count must stick. An attempt that finds the account busy is refused unchecked and
    /// uncounted (<see cref="BusyMessage"/>). The bootstrap token holder waits its turn instead.
    /// </summary>
    private async Task<Outcome<IdentityUser>> VerifyAsync(SignInCommand command, bool breakGlass, CancellationToken ct)
    {
        var email = users.NormalizeEmail(command.Email);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (breakGlass)
            await AccountLock.AcquireAsync(db, email, ct);
        else if (!await AccountLock.TryAcquireAsync(db, email, ct))
            return Outcome<IdentityUser>.RateLimited(BusyMessage);

        var user = await users.FindByEmailAsync(command.Email);
        bool verified;
        if (user is not null && breakGlass && await users.IsInRoleAsync(user, SkanyxxRoles.Owner))
            verified = await CheckIgnoringLockoutAsync(user, command.Password);
        // A locked-out account is refused before the password is hashed; hash anyway so it is not the fast answer.
        else if (user is null || await users.IsLockedOutAsync(user))
            verified = SpendHashTime(command.Password);
        else
            // Counts a failure toward lockout; the attempt that reaches the limit locks the account.
            verified = (await signIn.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true)).Succeeded;

        await transaction.CommitAsync(ct);
        return verified ? Outcome<IdentityUser>.Ok(user!) : Outcome<IdentityUser>.Unauthorized(SignInFailure.Message);
    }

    /// <summary>Break-glass (D081): the owner with the bootstrap token gets past a lockout an attacker keeps renewing.</summary>
    private async Task<bool> CheckIgnoringLockoutAsync(IdentityUser owner, string password)
    {
        if (await users.CheckPasswordAsync(owner, password))
            return true;

        var counted = await users.AccessFailedAsync(owner);
        if (!counted.Succeeded)
            throw new InvalidOperationException($"Counting a failed sign-in failed: {string.Join(", ", counted.Errors.Select(e => e.Code))}.");
        return false;
    }

    private bool SpendHashTime(string password)
    {
        failure.SpendHashTime(password);
        return false;
    }
}
