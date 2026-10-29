using System.Globalization;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// The token quota (<see cref="IChatQuota"/>) for every model turn run in a person's name: a person at or over their
/// effective limit is refused before the turn starts, as is one already running <see cref="QuotaInFlight.MaxTurns"/>
/// turns (D382), and every turn's tokens are added after it. People without a limit are never refused. Recording is
/// bounded to <see cref="Budget"/> and never fails a turn; a lost count is logged.
/// </summary>
internal sealed class DbChatQuota(
    IOrgMembership membership, QuotaLedger ledger, QuotaStore store, QuotaInFlight inFlight, TimeProvider clock, ILogger<DbChatQuota> logger)
    : IChatQuota
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    public static readonly string Busy = string.Create(CultureInfo.InvariantCulture,
        $"You already have {QuotaInFlight.MaxTurns} answers on their way. Wait for one to finish, then send this again: every answer counts against your token limit.");

    public async Task<ChatQuotaAdmission> AdmitAsync(string userId, string agentRef, CancellationToken ct)
    {
        var (quota, month) = await QuotaAsync(userId, ct);
        if (quota.Quota.Limit is null)
            return ChatQuotaAdmission.Admitted();
        if (quota.Over)
            return ChatQuotaAdmission.Refused(Refusal(quota, month));
        return inFlight.TryEnter(userId) is { } place
            ? new ChatQuotaAdmission(null, () => inFlight.Leave(userId, place))
            : ChatQuotaAdmission.Refused(Busy);
    }

    public async Task<string?> RefusalAsync(string userId, CancellationToken ct)
    {
        var (quota, month) = await QuotaAsync(userId, ct);
        return quota.Quota.Limit is not null && quota.Over ? Refusal(quota, month, "so no agent can work for you now") : null;
    }

    private async Task<(PersonQuota Quota, DateOnly Month)> QuotaAsync(string userId, CancellationToken ct)
    {
        var month = QuotaMonth.Of(clock.GetUtcNow());
        var org = await membership.ForUserAsync(userId, ct);
        return ((await ledger.BookAsync(month, [userId], ct)).For(userId, org.Teams, org.Departments), month);
    }

    public async Task RecordAsync(ChatQuotaUsage usage, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Budget);
        try
        {
            await store.AddUsageAsync(QuotaMonth.Of(clock.GetUtcNow()), usage, deadline.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not add a turn of {UserId} to the token quota", usage.UserId);
        }
    }

    public static string Refusal(PersonQuota quota, DateOnly month, string blocked = "so no agent can answer this now")
    {
        var binding = quota.Quota.Binding!;
        var whose = binding.Kind switch
        {
            QuotaScopes.User => "your own limit",
            QuotaScopes.Team => "your team's limit",
            _ => "your department's limit"
        };
        return string.Create(CultureInfo.InvariantCulture,
            $"You have used {quota.Used:N0} of your {quota.Quota.Limit:N0} tokens this month ({whose}), {blocked}. " +
            $"Use \"Request more\" (on Chat or your account page) to ask for more tokens, or wait until the quota resets on {QuotaMonth.ResetsOn(month):d MMMM} (UTC).");
    }
}
