using FluentValidation;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Engine;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

/// <summary>
/// A pipeline is checked as a whole at the boundary, where the full stage list is visible: loops must go
/// backwards and be bounded, ids must be unique, and every skill must exist.
/// </summary>
internal sealed class SavePipelineValidator : AbstractValidator<SavePipelineCommand>
{
    public SavePipelineValidator(PromptBuilder prompts, IOptions<TicketsOptions> options)
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Id).Must(PipelineRules.IsSlug).OverridePropertyName("id").WithMessage("Invalid pipeline id.");
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200).NoNul().OverridePropertyName("name");
        RuleFor(c => c.Description).NotNull().MaximumLength(2000).NoNul().OverridePropertyName("description");
        RuleFor(c => c.Stages).Cascade(CascadeMode.Stop).NotNull()
            .Must(s => s.Count is > 0 and <= PipelineRules.MaxStages).WithMessage($"A pipeline has 1 to {PipelineRules.MaxStages} stages.")
            .Must(s => s.All(stage => stage is not null)).WithMessage("Stages must not contain null entries.")
            .Must(s => s.Select(stage => stage.Id).Distinct().Count() == s.Count).WithMessage("Stage ids must be unique.")
            .OverridePropertyName("stages");
        RuleForEach(c => c.Stages).ChildRules(stage =>
        {
            stage.RuleFor(s => s.Id).Must(PipelineRules.IsSlug).WithMessage("Invalid stage id.");
            stage.RuleFor(s => s.Kind).IsInEnum();
            stage.RuleFor(s => s.Gate).IsInEnum();
            stage.RuleFor(s => s.Context).IsInEnum();
            stage.RuleFor(s => s.OnFail).IsInEnum();
            stage.RuleFor(s => s.Title).NotNull().MaximumLength(200).NoNul();
            stage.RuleFor(s => s.Description).NotNull().MaximumLength(2000).NoNul();
            stage.RuleFor(s => s.Instructions).NotNull().MaximumLength(8000).NoNul();
            stage.RuleFor(s => s.MaxLoops).NotNull().When(s => s.OnFail == OnFail.Goto)
                .WithMessage("on_fail: goto needs max_loops: a loop is bounded and says how far.");
            stage.RuleFor(s => s.MaxLoops).InclusiveBetween(0, PipelineRules.MaxLoops);
            stage.RuleFor(s => s.Skills).Cascade(CascadeMode.Stop).NotNull()
                .Must(k => k.All(key => key is not null)).WithMessage("Skills must not contain null entries.")
                .Must(k => k.All(prompts.SkillKeys.Contains)).WithMessage($"Skills must be among: {string.Join(", ", prompts.SkillKeys)}.")
                .Must(k => k.Distinct().Count() == k.Count).WithMessage("A skill may appear once.");
            stage.RuleFor(s => s.Agents).Cascade(CascadeMode.Stop).NotNull()
                .Must(a => a.Count is > 0 and <= PipelineRules.MaxAgentsPerStage)
                .WithMessage($"A stage has 1 to {PipelineRules.MaxAgentsPerStage} agents.")
                .Must(a => a.All(agent => agent is not null && PipelineRules.IsSlug(agent.Id) && PipelineRules.IsAgentRef(agent.Agent)))
                .WithMessage("Each agent needs an id and a kagent agent as 'namespace/name'.")
                .Must(a => a.Select(agent => agent.Id).Distinct().Count() == a.Count).WithMessage("Agent ids must be unique within a stage.")
                .Must(a => a.All(agent => options.Value.AllowedAgents.Contains(agent.Agent)))
                .WithMessage("Every agent must be in Tickets:AllowedAgents.");
            stage.RuleFor(s => s.Goto).Null().When(s => s.OnFail != OnFail.Goto).WithMessage("goto is only valid with on_fail: goto.");
        }).OverridePropertyName("stages");
        RuleFor(c => c).Must(LoopsGoBackwards).When(c => c.Stages is not null && c.Stages.All(s => s is not null))
            .OverridePropertyName("stages")
            .WithMessage("on_fail: goto must name an EARLIER stage of this pipeline.");
    }

    private static bool LoopsGoBackwards(SavePipelineCommand command)
    {
        var ids = command.Stages.Select(s => s.Id).ToList();
        return command.Stages.Select((s, i) => (s, i))
            .Where(x => x.s.OnFail == OnFail.Goto)
            .All(x => ids.IndexOf(x.s.Goto ?? "") is var target && target >= 0 && target < x.i);
    }
}
