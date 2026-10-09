using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;
using Skanyxx.Module.Identity.Passwords;

namespace Skanyxx.Module.Identity.Features.Passwords;

internal sealed class PasswordResetStatusHandler(
    AccountsDbContext db, IdentityAudit audit, ClientAddress client, TimeProvider time, ILogger<PasswordResetStatusHandler> logger)
    : IRequestHandler<PasswordResetStatusQuery, Outcome<PasswordResetDetails>>
{
    /// <summary>The one answer for unknown, used, revoked and expired links.</summary>
    public const string Invalid = "This password-reset link is invalid or has expired. Ask for a new one.";

    public async Task<Outcome<PasswordResetDetails>> Handle(PasswordResetStatusQuery query, CancellationToken ct)
    {
        var hash = ResetTokens.Hash(query.Token);
        var now = time.GetUtcNow();
        var details = await db.PasswordResets.AsNoTracking()
            .Where(r => r.TokenHash == hash && r.UsedUtc == null && r.RevokedUtc == null && r.ExpiresUtc > now)
            .Join(db.Users, r => r.UserId, u => u.Id, (r, u) => new PasswordResetDetails(u.Email!, r.ExpiresUtc))
            .SingleOrDefaultAsync(ct);
        if (details is not null)
            return Outcome<PasswordResetDetails>.Ok(details);

        // Never the token: this line is where guessing shows, next to the 429s.
        logger.LogWarning("Password-reset lookup refused from {RemoteIp}: invalid", client.Current);
        await audit.WriteSampledAsync(AuditActions.ResetRefused, "lookup", null, null, new { reason = "lookup: invalid link" }, ct);
        return Outcome<PasswordResetDetails>.NotFound(Invalid);
    }
}
