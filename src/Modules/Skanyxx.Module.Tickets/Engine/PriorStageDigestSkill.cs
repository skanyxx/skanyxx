using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Engine;

/// <summary>
/// Earlier stages' output. The work a stage acts on — every plan and code stage, and the stage just before it —
/// is shown in full up to <see cref="FullStageChars"/>; other reports (reviews, QA) are digested. Digesting the
/// thing being acted on once made QA judge 21% of a change and loop on "no tests" that were in the other 79%.
/// Every cut says so, in the text and as a warning.
/// </summary>
public sealed class PriorStageDigestSkill : IStageSkill
{
    public const string SkillKey = "prior_stage_digest";
    public const int DigestChars = 2_000;
    public const int FullStageChars = 20_000;

    public string Key => SkillKey;
    public string Description => "Earlier stages' output; plans, code and the latest stage in full, other reports digested.";

    public SkillBlock Run(SkillContext context)
    {
        if (context.Prior.Count == 0)
            return new SkillBlock("Prior stages", "_This is the first stage of the run._");

        List<string> blocks = [];
        List<string> warnings = [];
        for (var i = 0; i < context.Prior.Count; i++)
        {
            var p = context.Prior[i];
            var body = p.Output.Trim();
            var limit = p.Kind is StageKind.Plan or StageKind.Code || i == context.Prior.Count - 1 ? FullStageChars : DigestChars;
            var note = "";
            if (body.Length > limit)
            {
                warnings.Add($"prior stage '{p.Title}' was truncated to {limit:N0} of {body.Length:N0} characters in this prompt");
                note = $"\n\n_(truncated: showing {limit:N0} of {body.Length:N0} characters — you are reading a FRAGMENT " +
                       "of this stage's output, so do not conclude that something is missing from it)_";
                body = TextCut.Take(body, limit);
            }

            blocks.Add($"### {p.Title} ({p.Kind.ToString().ToLowerInvariant()})\n\n{context.Fence(body)}{note}");
        }

        return new SkillBlock("Prior stages", string.Join("\n\n", blocks), warnings);
    }
}
