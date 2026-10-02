namespace Skanyxx.Module.Tickets.Engine;

/// <summary>The ticket's structured fields as a flat, quotable list. No model call: the fields are already structured.</summary>
public sealed class TicketFactsSkill : IStageSkill
{
    public const string SkillKey = "ticket_facts";

    public string Key => SkillKey;
    public string Description => "Restate the ticket's structured fields as a flat, quotable list.";

    public SkillBlock Run(SkillContext context)
    {
        var t = context.Ticket;
        return new SkillBlock("Ticket facts", context.Fence(string.Join('\n',
            $"- key: {t.Key}",
            $"- type: {Or(t.Type)}",
            $"- status: {Or(t.Status)}",
            $"- priority: {Or(t.Priority)}",
            $"- assignee: {t.Assignee ?? "unassigned"}",
            $"- labels: {(t.Labels.Count > 0 ? string.Join(", ", t.Labels) : "none")}",
            $"- description present: {(t.Description.Trim().Length > 0 ? "yes" : "no")}")));
    }

    private static string Or(string value) => value.Length > 0 ? value : "unknown";
}
