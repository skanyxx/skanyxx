using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Email;
using Skanyxx.Module.Identity.Features.People;
using Skanyxx.Module.Identity.Passwords;

namespace Skanyxx.Module.Identity.Features.Passwords;

/// <summary>
/// D157: the owner issues a reset link for a person, under the account lock, audited in the same transaction. With
/// SMTP the link goes to the person's email and the owner never sees it; without SMTP (or if sending fails) the owner
/// gets it once, like an invite link, and passes it on. Issuing changes nothing else: the old password works until the
/// link is used.
/// </summary>
internal sealed class IssuePasswordResetHandler(
    AccountsDbContext db, UserManager<IdentityUser> users, PasswordResets resets, InviteLinks links, LinkMail mail, IdentityAudit audit,
    ClientAddress client, ILogger<IssuePasswordResetHandler> logger)
    : IRequestHandler<IssuePasswordResetCommand, Outcome<PasswordResetIssued>>
{
    public async Task<Outcome<PasswordResetIssued>> Handle(IssuePasswordResetCommand command, CancellationToken ct)
    {
        if (!links.CanBuild)
            return Outcome<PasswordResetIssued>.Conflict(null, InviteLinks.NotConfigured);

        string token, email;
        DateTimeOffset expires;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            if (await LockedAccount.LoadAsync(db, users, command.UserId, ct) is not { } user)
                return Outcome<PasswordResetIssued>.NotFound("No person has that id.");
            if (await resets.RefusalAsync(user, ct) is { } refused)
                return new Outcome<PasswordResetIssued>(refused.Status, Message: refused.Message);

            (token, expires) = await resets.IssueAsync(user.Id, command.ActorId, ct);
            await audit.WriteAsync(AuditActions.ResetIssued, command.ActorId, user.Id,
                new { expiresUtc = expires, delivery = mail.Enabled ? "email" : "link shown to the owner" }, ct);
            await transaction.CommitAsync(ct);
            email = user.Email!;
        }

        var link = links.ResetFor(token);
        var emailed = await mail.PasswordResetAsync(email, link, expires, ct);
        // L1: the row above said "email"; the owner is about to hold the person's credential after all.
        if (mail.Enabled && !emailed)
            await audit.WriteAsync(AuditActions.ResetLinkShown, command.ActorId, command.UserId, new { reason = "the email could not be sent" }, CancellationToken.None);
        logger.LogWarning("Password-reset link for {UserId} issued by {ActorUserId} from {RemoteIp}, {Delivery}", command.UserId, command.ActorId,
            client.Current, emailed ? "emailed to the person" : "shown to the owner");
        return Outcome<PasswordResetIssued>.Created(new PasswordResetIssued(emailed ? null : link, expires, emailed));
    }
}
