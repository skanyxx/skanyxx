namespace Skanyxx.Module.Tickets.Engine;

/// <summary>A fixed checklist chosen by ticket type. Its value is that it is the same every time.</summary>
public sealed class RiskChecklistSkill : IStageSkill
{
    public const string SkillKey = "risk_checklist";

    private static readonly string[] Common =
    [
        "Does the change alter data that already exists?",
        "Is there a failure mode that is silent rather than loud?",
        "What is the smallest reversible version of this?"
    ];

    private static readonly Dictionary<string, string[]> ByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bug"] =
        [
            "Is the reported symptom reproduced before anything is changed?",
            "Is the fix at the cause or at the symptom?",
            "Does a regression test fail without the fix?"
        ],
        ["epic"] =
        [
            "Is this decomposable into independently shippable pieces?",
            "Which piece proves the riskiest assumption first?"
        ]
    };

    public string Key => SkillKey;
    public string Description => "A fixed review checklist selected from the ticket's type.";

    public SkillBlock Run(SkillContext context)
    {
        string[] items = [.. ByType.GetValueOrDefault(context.Ticket.Type.Trim(), []), .. Common];
        return new SkillBlock("Risk checklist", string.Join('\n', items.Select(i => $"- {i}")));
    }
}
