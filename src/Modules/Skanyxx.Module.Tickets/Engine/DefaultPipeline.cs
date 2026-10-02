using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// Plan → Review the plan → Code → QA (loops back to Code, at most twice) → human-gated Review.
/// The stage agents are the kagent Agents in <c>deploy/kagent/ticket-flow/</c>.
/// </summary>
public static class DefaultPipeline
{
    public const string Id = "ticket-fix";
    public const string AgentNamespace = "kagent";

    /// <summary>The agents in <c>deploy/kagent/ticket-flow/</c>: the default allow-list.</summary>
    public static readonly string[] Agents =
    [
        $"{AgentNamespace}/ticket-planner", $"{AgentNamespace}/ticket-plan-reviewer", $"{AgentNamespace}/ticket-coder",
        $"{AgentNamespace}/ticket-qa", $"{AgentNamespace}/ticket-reviewer"
    ];

    public static Pipeline Create(DateTime now) => new()
    {
        Id = Id,
        Name = "Plan → Review → Code → QA → Review",
        Description = "Plan the ticket, review the plan, write the change, QA it — and send it back to be rewritten " +
                      "if QA fails — then a human-gated senior review.",
        CreatedAt = now,
        UpdatedAt = now,
        Stages =
        [
            new()
            {
                Id = "plan", Kind = StageKind.Plan, Title = "Plan",
                Description = "Turn the ticket into a plan a developer can follow.",
                Skills = [TicketFactsSkill.SkillKey], Agents = [Agent("ticket-planner")]
            },
            // Review, not QA: at this point the plan IS the spec, so there is nothing to test it against.
            new()
            {
                Id = "review-plan", Kind = StageKind.Review, Title = "Review the plan",
                Description = "Is this the right approach, before anything is written?",
                Skills = [PriorStageDigestSkill.SkillKey], Context = ContextMode.Last,
                Agents = [Agent("ticket-plan-reviewer")]
            },
            new()
            {
                Id = "code", Kind = StageKind.Code, Title = "Code",
                Description = "Write the change the plan describes.",
                Skills = [PriorStageDigestSkill.SkillKey], Agents = [Agent("ticket-coder")]
            },
            new()
            {
                Id = "qa-code", Kind = StageKind.Qa, Title = "QA the change",
                Description = "Does the change do what the plan said, with tests?",
                Skills = [PriorStageDigestSkill.SkillKey, RiskChecklistSkill.SkillKey],
                FailOnVerdict = true, OnFail = OnFail.Goto, Goto = "code", MaxLoops = 2,
                Agents = [Agent("ticket-qa")]
            },
            new()
            {
                Id = "review", Kind = StageKind.Review, Title = "Review",
                Description = "Final senior review of the whole run.",
                Skills = [PriorStageDigestSkill.SkillKey], Gate = Gate.Human,
                Agents = [Agent("ticket-reviewer")]
            }
        ]
    };

    private static StageAgent Agent(string name) => new(name, $"{AgentNamespace}/{name}");
}
