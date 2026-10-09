using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Entra;

namespace Skanyxx.Module.Identity.Features.SignIn;

internal sealed class SignInHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, SignInFailure failure,
    SessionIssuer sessions, BootstrapGuard guard, EntraPasswordRule entra, IdentityAudit audit, ILogger<SignInHandler> logger)
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
        {
            logger.LogWarning("Sign-in with a bootstrap token from {Client}: token {Token}, result {Result}.",
                signIn.Context.Connection.RemoteIpAddress, breakGlass ? "valid" : "wrong", verified.Status);
            // After the attempt's own transaction: whatever it was, the row stays (never the token, never the email).
            await audit.WriteAsync(AuditActions.BootstrapTokenSignIn, verified.Value?.Id, verified.Value?.Id,
                new { token = breakGlass ? "valid" : "wrong", result = verified.Status.ToString() }, ct);
        }
        return verified.Value is { } user
            ? Outcome<SignedIn>.Ok(await sessions.StartAsync(user, command.UseCookie, ct))
            : new Outcome<SignedIn>(verified.Status, Message: verified.Message);
    }

    /// <summary>
    /// The password check, one attempt per account at a time (<see cref="AccountLock"/>), committed whatever the
    /// answer: a failed attempt's count must stick. An attempt that finds the account busy is refused unchecked and
    /// uncounted (<see cref="BusyMessage"/>). The bootstrap token holder waits its turn instead. An account that signs in
    /// with Microsoft (D8) gets the same answer as a wrong password, right password or not (SEC N3).
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
        else if (await entra.RefusesAsync(user, ct))
            verified = await RefuseManagedAsync(user, command.Password, ct);
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

        await CountFailureAsync(owner);
        return false;
    }

    /// <summary>
    /// SEC N3: an Entra-managed account's password never starts a session while Microsoft sign-in is enabled (D8), and
    /// the answer is the one a wrong password gets, so it tells a guesser nothing. Every attempt counts toward the
    /// lockout and writes the same audit row, right password or wrong, so both do the same database work and take the
    /// same time (QA-3 L1, D166); a lockout does not block Microsoft sign-in. Both log the same Warning; only a Debug line
    /// says the password was right (QA-2 L1).
    /// </summary>
    private async Task<bool> RefuseManagedAsync(IdentityUser user, string password, CancellationToken ct)
    {
        var right = await users.CheckPasswordAsync(user, password);
        await audit.WriteAsync(AuditActions.ManagedPasswordRefused, null, user.Id, null, ct);
        await CountFailureAsync(user);
        logger.LogWarning("Password sign-in of {UserId} from {Client} refused: the account signs in with Microsoft (D8)",
            user.Id, signIn.Context.Connection.RemoteIpAddress);
        if (right)
            logger.LogDebug("The refused password sign-in of {UserId} had the right password", user.Id);
        return false;
    }

    private async Task CountFailureAsync(IdentityUser user)
    {
        var counted = await users.AccessFailedAsync(user);
        if (!counted.Succeeded)
            throw new InvalidOperationException($"Counting a failed sign-in failed: {string.Join(", ", counted.Errors.Select(e => e.Code))}.");
    }

    private bool SpendHashTime(string password)
    {
        failure.SpendHashTime(password);
        return false;
    }
}
