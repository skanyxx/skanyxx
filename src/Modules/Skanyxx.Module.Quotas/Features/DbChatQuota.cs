using System.Globalization;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform.Chat;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Quotas.Data;

namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// Chat's quota (<see cref="IChatQuota"/>): a person at or over their effective limit is refused before the turn starts,
/// and every turn's tokens are added after it. Recording is bounded to <see cref="Budget"/> and never fails a chat; a
/// lost count is logged.
/// </summary>
internal sealed class DbChatQuota(IOrgMembership membership, QuotaLedger ledger, QuotaStore store, TimeProvider clock, ILogger<DbChatQuota> logger)
    : IChatQuota
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    public async Task<string?> RefusalAsync(string userId, string agentRef, CancellationToken ct)
    {
        var month = QuotaMonth.Of(clock.GetUtcNow());
        var org = await membership.ForUserAsync(userId, ct);
        var quota = (await ledger.BookAsync(month, [userId], ct)).For(userId, org.Teams, org.Departments);
        return quota.Over ? Refusal(quota, month) : null;
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
            logger.LogWarning(ex, "Could not add a chat turn of {UserId} to the token quota", usage.UserId);
        }
    }

    public static string Refusal(PersonQuota quota, DateOnly month)
    {
        var binding = quota.Quota.Binding!;
        var whose = binding.Kind switch
        {
            QuotaScopes.User => "your own limit",
            QuotaScopes.Team => "your team's limit",
            _ => "your department's limit"
        };
        return string.Create(CultureInfo.InvariantCulture,
            $"You have used {quota.Used:N0} of your {quota.Quota.Limit:N0} tokens this month ({whose}), so Chat cannot send this message. " +
            $"Use \"Request more\" to ask for more tokens, or wait until the quota resets on {QuotaMonth.ResetsOn(month):d MMMM}.");
    }
}
