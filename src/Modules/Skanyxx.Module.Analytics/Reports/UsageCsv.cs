using System.Globalization;
using System.Text;
using Skanyxx.Core.Platform.Analytics;
using Skanyxx.Core.Platform.Usage;
using Skanyxx.Module.Analytics.Data;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Reports;

/// <summary>
/// The rollup rows of a scope as CSV: fixed columns of counts, names and ids, never anything anyone wrote. The first line
/// is the cost disclaimer. Cells a spreadsheet would run as a formula are quoted with a leading apostrophe.
/// </summary>
public static class UsageCsv
{
    public static readonly string[] Columns =
    [
        "day", "user_id", "user", "agent", "model", "requests", "chats", "tokens_in", "tokens_out", "client_tokens_estimate",
        "turns_without_token_report", "cache_hits", "tokens_saved_estimate", "errors", "estimated_cost"
    ];

    public static string Write(IEnumerable<UsageDay> rows, OrgSnapshot org, CostCalculator cost)
    {
        var csv = new StringBuilder();
        csv.Append("# ").Append(CostDisclaimer.Text).Append('\n');
        csv.AppendJoin(',', Columns).Append('\n');
        foreach (var r in rows.OrderBy(r => r.Day).ThenBy(r => r.UserId, StringComparer.Ordinal).ThenBy(r => r.Agent, StringComparer.Ordinal)
                     .ThenBy(r => r.Model, StringComparer.Ordinal))
        {
            var estimate = cost.Estimate([r]);
            string[] cells =
            [
                r.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), r.UserId, org.PersonName(r.UserId), r.Agent, r.Model,
                Number(r.Requests), Number(r.Chats), Number(r.TokensIn), Number(r.TokensOut), Number(r.ClientTokens), Number(r.Unreported),
                Number(r.CacheHits), Number(r.TokensSaved), Number(r.Errors),
                estimate.Priced ? $"{estimate.Amount.ToString(CultureInfo.InvariantCulture)} {estimate.Currency}" : ""
            ];
            csv.AppendJoin(',', cells.Select(Cell)).Append('\n');
        }
        return csv.ToString();
    }

    private static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);

    internal static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
