using FluentValidation;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Workflows;

internal sealed class SaveWorkflowValidator : AbstractValidator<SaveWorkflowCommand>
{
    public SaveWorkflowValidator(IOptions<InvestigateOptions> options)
    {
        var limits = options.Value;
        RuleFor(c => c.Viewer).SignedIn();
        RuleFor(c => c.Name).Must(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 120 && !n.Contains('\0'))
            .OverridePropertyName("name").WithMessage("Give the workflow a name of at most 120 characters.");
        RuleFor(c => c.Description).Must(d => d is null || (d.Length <= 1000 && !d.Contains('\0')))
            .OverridePropertyName("description").WithMessage("The description is at most 1000 characters.");
        RuleFor(c => c.SharedTeam).Must(t => t is null || StepId.IsValid(t))
            .OverridePropertyName("sharedTeam").WithMessage("Not a team.");
        RuleFor(c => c).Custom((c, context) =>
        {
            foreach (var error in WorkflowRules.Validate(c.Steps, limits.MaxDepth, limits.MaxSteps, limits.MaxPromptChars))
                context.AddFailure("steps", error);
        });
    }
}
