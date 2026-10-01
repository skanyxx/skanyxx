using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Features.People;

namespace Skanyxx.Module.Identity.Entra;

/// <summary>
/// The account side of a Microsoft sign-in, each under the account's lock in one transaction, the way the People
/// commands change accounts. An Entra-managed account (one with an <c>entra</c> login, D1) gets exactly the mapped roles
/// and teams; the owner is never changed by the mapping. A role change ends the account's other sessions. Taking
/// <c>supervisor</c> away marks a <see cref="Core.Platform.Identity.PrivilegesRevoked"/> as owed in the same transaction;
/// after the commit it is published only when one is owed (this change's, or an earlier failed one: the retry), so a
/// Microsoft sign-in that takes nothing away does not depend on the memory database (D13, replacing D12).
/// </summary>
internal sealed class EntraAccounts(
    AccountsDbContext db, UserManager<IdentityUser> users, SessionRevocation sessions, PrivilegeRevocation revocation,
    ClientAddress client, TimeProvider time, ILogger<EntraAccounts> logger)
{
    public const string EmailTaken =
        "An account with this email already exists. Sign in with your password, then link your Microsoft account on your Account page.";

    public const string UnusableEmail = "Your Microsoft account has no email address Skanyxx can use as an account name. Ask the owner for an invite.";

    public const string AlreadyLinked = "This account is already linked to a Microsoft account.";

    public const string LinkedElsewhere = "That Microsoft account is already linked to another Skanyxx account.";

    public const string EmailNotVerified =
        "Your organisation does not verify the email address on your Microsoft account, so Skanyxx cannot create an account for it. " +
        "Ask the owner for an invite, then link your Microsoft account on your Account page.";

    public const string OwnerStaysLocal = "The owner signs in with a password only: a Microsoft account cannot be linked to it.";

    public const int MaxDisplayName = 100;

    private const string SupervisorLost = "Entra group mapping without supervisor";

    private const string RefusedReason = "Microsoft sign-in refused";

    private const string RetryHint = "the next Microsoft sign-in of this account publishes again";

    /// <summary>
    /// One Entra identity (tid|oid) at a time, whichever account a callback is about to bind it to (CR L4). Listed with
    /// every other advisory-lock key in Skanyxx.Module.Memory's MemoryMigrator.
    /// </summary>
    internal const long LoginLockSeed = 0x49444E05;

    private static readonly EntraMapping NoGroups = new(EntraAccountKind.Member, 0, [], []);

    /// <summary>
    /// A new account from a first Microsoft sign-in (D4, <c>201</c>), or <c>409</c> when the email already has one (never
    /// linked by email, D3). <c>200</c> with the account: another callback created or linked this Microsoft account while
    /// this one waited, and the caller treats it as an existing account (re-mapped, disabled checked).
    /// </summary>
    public async Task<Outcome<IdentityUser>> CreateAsync(ExternalLoginInfo info, EntraMapping mapping, CancellationToken ct)
    {
        if (EmailOf(info.Principal) is not { } email)
            return Outcome<IdentityUser>.Forbidden(UnusableEmail);
        if (EntraClaims.EmailUnverified(info.Principal))
        {
            logger.LogWarning("Microsoft sign-in {Key} from {RemoteIp} refused: xms_edov says the email's domain is not verified", info.ProviderKey, client.Current);
            return Outcome<IdentityUser>.Forbidden(EmailNotVerified);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockLoginAsync(info.ProviderKey, ct);
        if (await users.FindByLoginAsync(EntraScheme.Name, info.ProviderKey) is { } raced)
            return Outcome<IdentityUser>.Ok(raced);
        await AccountLock.AcquireAsync(db, users.NormalizeEmail(email), ct);
        if (await users.FindByEmailAsync(email) is not null)
        {
            logger.LogWarning("Microsoft sign-in {Key} from {RemoteIp} refused: the email belongs to an existing account without this login",
                info.ProviderKey, client.Current);
            return Outcome<IdentityUser>.Conflict(null, EmailTaken);
        }

        var user = new IdentityUser { UserName = email, Email = email };
        if (!(await users.CreateAsync(user)).Succeeded)
            return Outcome<IdentityUser>.Forbidden(UnusableEmail);
        LockedAccount.Require(await users.AddLoginAsync(user, new UserLoginInfo(EntraScheme.Name, info.ProviderKey, EntraScheme.DisplayName)));
        if (DisplayNameOf(info.Principal) is { } name)
            LockedAccount.Require(await users.AddClaimAsync(user, AccountReader.DisplayNameClaim(name)));
        var changes = await ApplyAsync(user, mapping, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Account {UserId} created by Microsoft sign-in {Key} from {RemoteIp} with roles {Roles} and teams {Teams}",
            user.Id, info.ProviderKey, client.Current, changes!.AddedRoles, changes.AddedTeams);
        return Outcome<IdentityUser>.Created(user);
    }

    /// <summary>
    /// Re-maps an Entra-managed account at sign-in (D1). Null when it is disabled, or when its Microsoft login was
    /// removed after the caller found it (CR m1): nothing changes and there is no session.
    /// </summary>
    public async Task<IdentityUser?> SyncAsync(string userId, string key, EntraMapping mapping, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await LockedLoginAsync(userId, key, ct);
        if (user is null || AccountStatus.IsDisabled(user))
        {
            logger.LogWarning("Microsoft sign-in of {UserId} ({Key}) from {RemoteIp} refused: {Reason}", userId, key, client.Current,
                user is null ? "the Microsoft login is no longer on the account" : "account disabled");
            return null;
        }

        var changes = await ApplyAsync(user, mapping, ct);
        if (changes?.RolesChanged == true)
            await sessions.EndAllAsync(user, ct);
        var lost = await RecordSupervisorAsync(user, mapping, changes, ct);
        await transaction.CommitAsync(ct);

        Audit(user.Id, key, "re-mapped at Microsoft sign-in", changes);
        await revocation.SettleAsync(user.Id, lost ? SupervisorLost : PrivilegeRevocation.Retried, EntraScheme.Actor, RetryHint);
        return user;
    }

    /// <summary>
    /// A refused sign-in of an Entra-managed account (D2): the empty mapping applies (no roles, no teams) and every
    /// session ends; a revocation is published when one is owed (supervisor taken away now, or an earlier failure). The
    /// account stays. The owner is untouched, and an account that lost its Microsoft login meanwhile is left alone (CR m1).
    /// </summary>
    public async Task RefuseAsync(string userId, string key, string reason, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await LockedLoginAsync(userId, key, ct) is not { } user)
        {
            logger.LogWarning("Microsoft sign-in of {UserId} ({Key}) from {RemoteIp} refused ({Reason}); the Microsoft login is no longer on the account",
                userId, key, client.Current, reason);
            return;
        }

        var changes = await ApplyAsync(user, NoGroups, ct);
        if (changes is not null)
            await sessions.EndAllAsync(user, ct);
        var lost = await RecordSupervisorAsync(user, NoGroups, changes, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Microsoft sign-in of {UserId} ({Key}) from {RemoteIp} refused ({Reason}): {Effect}", user.Id, key, client.Current, reason,
            changes is null ? "the owner is not changed by the mapping" : $"roles removed [{string.Join(", ", changes.RemovedRoles)}], teams removed [{string.Join(", ", changes.RemovedTeams)}], sessions ended");
        if (changes is not null)
            await revocation.SettleAsync(user.Id, lost ? RefusedReason : PrivilegeRevocation.Retried, EntraScheme.Actor, RetryHint);
    }

    /// <summary>
    /// Adds the Microsoft login to a signed-in person's own account (D3) and applies the mapping. Every earlier session
    /// ends: they were password sessions, which an Entra-managed account no longer gets while Microsoft sign-in is on (D8).
    /// Never the owner (D11).
    /// </summary>
    public async Task<Outcome<IdentityUser>> LinkAsync(string userId, string key, EntraMapping mapping, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockLoginAsync(key, ct);
        if (await LockedAccount.LoadAsync(db, users, userId, ct) is not { } user)
            return Outcome<IdentityUser>.NotFound("No person has that id.");
        if (await users.IsInRoleAsync(user, SkanyxxRoles.Owner))
            return Outcome<IdentityUser>.Forbidden(OwnerStaysLocal);
        if ((await users.GetLoginsAsync(user)).Any(l => l.LoginProvider == EntraScheme.Name))
            return Outcome<IdentityUser>.Conflict(null, AlreadyLinked);
        if (await users.FindByLoginAsync(EntraScheme.Name, key) is not null)
            return Outcome<IdentityUser>.Conflict(null, LinkedElsewhere);

        LockedAccount.Require(await users.AddLoginAsync(user, new UserLoginInfo(EntraScheme.Name, key, EntraScheme.DisplayName)));
        var changes = await ApplyAsync(user, mapping, ct);
        await sessions.EndAllAsync(user, ct);
        var lost = await RecordSupervisorAsync(user, mapping, changes, ct);
        await transaction.CommitAsync(ct);

        logger.LogWarning("Account {UserId} linked to Microsoft sign-in {Key} from {RemoteIp}; its other sessions ended", user.Id, key, client.Current);
        Audit(user.Id, key, "mapped on linking", changes);
        await revocation.SettleAsync(user.Id, lost ? SupervisorLost : PrivilegeRevocation.Retried, EntraScheme.Actor, RetryHint);
        return Outcome<IdentityUser>.Ok(user);
    }

    /// <summary>
    /// Replaces the account's roles and team memberships with the mapping's. Null for the owner, whose roles and teams
    /// never come from the mapping. Must run inside the caller's transaction, under the account lock.
    /// </summary>
    private async Task<EntraRoleChanges?> ApplyAsync(IdentityUser user, EntraMapping mapping, CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user);
        if (roles.Contains(SkanyxxRoles.Owner))
            return null;

        var removedRoles = roles.Except(mapping.Roles).ToList();
        var addedRoles = mapping.Roles.Except(roles).ToList();
        if (removedRoles.Count > 0)
            LockedAccount.Require(await users.RemoveFromRolesAsync(user, removedRoles));
        if (addedRoles.Count > 0)
            LockedAccount.Require(await users.AddToRolesAsync(user, addedRoles));

        var teams = await db.TeamMembers.Where(m => m.UserId == user.Id).Select(m => m.TeamSlug).ToListAsync(ct);
        var removedTeams = teams.Except(mapping.Teams).ToList();
        var addedTeams = mapping.Teams.Except(teams).ToList();
        if (removedTeams.Count > 0)
            await db.TeamMembers.Where(m => m.UserId == user.Id && removedTeams.Contains(m.TeamSlug)).ExecuteDeleteAsync(ct);
        foreach (var team in addedTeams)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity_org_team_members ("TeamSlug", "UserId", "AddedBy", "AddedUtc")
                VALUES ({team}, {user.Id}, {EntraScheme.Actor}, {time.GetUtcNow()}) ON CONFLICT ("TeamSlug", "UserId") DO NOTHING
                """, ct);
        return new EntraRoleChanges(addedRoles, removedRoles, addedTeams, removedTeams);
    }

    /// <summary>The audit line for a real change (none for the owner, <paramref name="changes"/> null).</summary>
    private void Audit(string userId, string key, string what, EntraRoleChanges? changes)
    {
        if (changes?.Any == true)
            logger.LogWarning("Account {UserId} ({Key}) {What} from {RemoteIp}: roles added {AddedRoles} removed {RemovedRoles}, teams added {AddedTeams} removed {RemovedTeams}{Sessions}",
                userId, key, what, client.Current, changes.AddedRoles, changes.RemovedRoles, changes.AddedTeams, changes.RemovedTeams,
                changes.RolesChanged ? "; other sessions ended" : "");
    }

    /// <summary>
    /// In the caller's transaction: <c>supervisor</c> was taken away, so a revocation is owed (D13, true); the mapping
    /// keeps it, so an owed one is dropped (D17). The owner (<paramref name="changes"/> null) is left alone.
    /// </summary>
    private async Task<bool> RecordSupervisorAsync(IdentityUser user, EntraMapping mapping, EntraRoleChanges? changes, CancellationToken ct)
    {
        if (changes is null)
            return false;
        if (changes.RemovedRoles.Contains(SkanyxxRoles.Supervisor))
        {
            await revocation.MarkAsync(user.Id, ct);
            return true;
        }
        if (mapping.Roles.Contains(SkanyxxRoles.Supervisor))
            await revocation.ForgiveAsync(user.Id, ct);
        return false;
    }

    /// <summary>
    /// The account under its lock, only while it still has the Microsoft login <paramref name="key"/>: the caller found
    /// it by that login before the lock, and the owner may have removed it since (CR m1).
    /// </summary>
    private async Task<IdentityUser?> LockedLoginAsync(string userId, string key, CancellationToken ct) =>
        await LockedAccount.LoadAsync(db, users, userId, ct) is { } user
        && (await users.GetLoginsAsync(user)).Any(l => l.LoginProvider == EntraScheme.Name && l.ProviderKey == key)
            ? user
            : null;

    private Task LockLoginAsync(string key, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, {LoginLockSeed}))", ct);

    /// <summary>The token's email, else a user principal name that is an address. Display and account name only: never a key (D4).</summary>
    private static string? EmailOf(ClaimsPrincipal principal) =>
        new[] { principal.FindFirstValue(EntraClaims.Email), principal.FindFirstValue(EntraClaims.PreferredUserName) }
            .Select(v => v?.Trim())
            .FirstOrDefault(v => v is { Length: > 2 and <= 256 } && v.IndexOf('@') > 0 && !v.Any(char.IsControl));

    /// <summary>
    /// The token's name, held to the display-name rules (no control or invisible formatting characters, at most
    /// <see cref="MaxDisplayName"/> UTF-16 units), cut between whole text elements so no surrogate pair or combining
    /// sequence is split (CR L6).
    /// </summary>
    internal static string? DisplayNameOf(ClaimsPrincipal principal)
    {
        var name = new string((principal.FindFirstValue(EntraClaims.Name) ?? "")
            .Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray()).Trim();
        var end = 0;
        var elements = StringInfo.GetTextElementEnumerator(name);
        while (elements.MoveNext() && elements.ElementIndex + elements.GetTextElement().Length <= MaxDisplayName)
            end = elements.ElementIndex + elements.GetTextElement().Length;
        return name[..end].Trim() is { Length: > 0 } kept ? kept : null;
    }
}
