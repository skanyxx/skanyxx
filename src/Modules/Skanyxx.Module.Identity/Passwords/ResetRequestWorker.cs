using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Email;

namespace Skanyxx.Module.Identity.Passwords;

/// <summary>
/// Turns queued "forgot your password?" requests into emails, one at a time: under the account lock, an account that may
/// reset (<see cref="PasswordResets.RefusalAsync"/>) and asked no more than once per <see cref="ResendAfter"/> gets a new
/// link (the older one revoked), audited in the same transaction; the email goes out after the commit. Unknown emails,
/// refused accounts and resends inside the window get nothing — the caller was already answered the same way.
/// </summary>
internal sealed class ResetRequestWorker(IServiceScopeFactory scopes, ResetRequestQueue queue, ILogger<ResetRequestWorker> logger) : BackgroundService
{
    /// <summary>One email per account per this window: a stranger cannot flood someone's inbox through the form.</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(request, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "A password-reset request from {RemoteIp} could not be handled", request.RemoteIp);
            }
        }
    }

    internal async Task HandleAsync(ResetRequest request, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AccountsDbContext>();
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var resets = services.GetRequiredService<PasswordResets>();
        var audit = services.GetRequiredService<IdentityAudit>();

        string to, token;
        DateTimeOffset expires;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await AccountLock.AcquireAsync(db, users.NormalizeEmail(request.Email), ct);
            var user = await users.FindByEmailAsync(request.Email);
            var refusal = user is null ? "no such account"
                : await resets.RefusalAsync(user, ct) is { } refused ? refused.Message
                : await resets.RecentAsync(user.Id, ResendAfter, ct) ? "a link was sent less than two minutes ago"
                : null;
            if (refusal is not null)
            {
                logger.LogInformation("Password-reset request from {RemoteIp}: no email sent ({Reason})", request.RemoteIp, refusal);
                // Anyone can send any email address: sampled per reason (D167).
                await audit.WriteSampledAsync(AuditActions.ResetRefused, refusal, null, user?.Id, new { reason = refusal, via = "email request" }, ct, request.RemoteIp);
                await transaction.CommitAsync(ct);
                return;
            }

            (token, expires) = await resets.IssueAsync(user!.Id, createdBy: null, ct);
            await audit.WriteAsync(AuditActions.ResetRequested, null, user.Id, new { expiresUtc = expires }, ct, request.RemoteIp);
            await transaction.CommitAsync(ct);
            to = user.Email!;
            logger.LogWarning("Password-reset link for {UserId} requested from {RemoteIp}", user.Id, request.RemoteIp);
        }

        await services.GetRequiredService<LinkMail>().PasswordResetAsync(to, InviteLinks.ResetFor(request.LinkBase, token), expires, ct);
    }
}
