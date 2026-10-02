using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// The verdict is the answer's first non-empty line, compared exactly (trailing whitespace aside). Nothing else in the
/// answer is ever read, so text the agent quotes from a ticket can neither supply nor override it; anything else is Unknown.
/// </summary>
public static class VerdictReader
{
    public static Verdict Read(StageKind kind, string body) => (kind, FirstLine(body)) switch
    {
        (StageKind.Qa, "VERDICT: PASS") => Verdict.Pass,
        (StageKind.Qa, "VERDICT: FAIL") => Verdict.Fail,
        (StageKind.Review, "RECOMMENDATION: SHIP" or "RECOMMENDATION: SHIP WITH CHANGES") => Verdict.Pass,
        (StageKind.Review, "RECOMMENDATION: DO NOT SHIP") => Verdict.Fail,
        _ => Verdict.Unknown
    };

    /// <summary>The sentence that states this contract to the agent; null for kinds that state no verdict.</summary>
    public static string? Instruction(StageKind kind) => kind switch
    {
        StageKind.Qa =>
            "Your answer's FIRST line must be exactly `VERDICT: PASS` or `VERDICT: FAIL` — no heading, no bold, no code fence, " +
            "nothing before it and nothing else on that line. Skanyxx reads only that line; a verdict written anywhere else " +
            "is ignored and counts as a failure.",
        StageKind.Review =>
            "Your answer's FIRST line must be exactly `RECOMMENDATION: SHIP`, `RECOMMENDATION: SHIP WITH CHANGES` or " +
            "`RECOMMENDATION: DO NOT SHIP` — no heading, no bold, no code fence, nothing before it and nothing else on that line. " +
            "Skanyxx reads only that line; a recommendation written anywhere else is ignored and counts as DO NOT SHIP.",
        _ => null
    };

    private static string FirstLine(string body) =>
        body.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.TrimEnd() ?? "";
}
