using FluentValidation;

namespace Skanyxx.Module.Investigate.Features.Workflows;

internal sealed class DuplicateWorkflowValidator : AbstractValidator<DuplicateWorkflowCommand>
{
    public DuplicateWorkflowValidator() => RuleFor(c => c.Viewer).SignedIn();
}
