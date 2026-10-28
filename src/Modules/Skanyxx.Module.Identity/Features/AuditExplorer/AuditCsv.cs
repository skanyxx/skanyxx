using System.Text;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// An export as CSV (RFC 4180, every cell quoted). A cell a spreadsheet would run as a formula (<c>= + - @</c>, tab,
/// CR) gets a leading apostrophe: audit text can come from anyone who reaches a refusal.
/// </summary>
internal static class AuditCsv
{
    public static readonly string[] Header = ["time", "action", "module", "outcome", "actor", "actorEmail", "target", "targetEmail", "ip", "details"];

    public static string Write(AuditExport export)
    {
        var csv = new StringBuilder();
        csv.AppendJoin(',', Header.Select(Cell)).Append("\r\n");
        foreach (var row in export.Rows)
            csv.AppendJoin(',', new[]
            {
                row.At.UtcDateTime.ToString("O"), row.Action, row.Module, row.Outcome, row.ActorId, export.Email(row.ActorId),
                row.TargetId, export.Email(row.TargetId), row.RemoteIp, row.Details?.GetRawText()
            }.Select(Cell)).Append("\r\n");
        return csv.ToString();
    }

    public static string Cell(string? value)
    {
        value ??= "";
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
