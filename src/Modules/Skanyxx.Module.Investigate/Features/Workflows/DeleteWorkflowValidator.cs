using FluentValidation;

namespace Skanyxx.Module.Investigate.Features.Workflows;

internal sealed class DeleteWorkflowValidator : AbstractValidator<DeleteWorkflowCommand>
{
    public DeleteWorkflowValidator() => RuleFor(c => c.Viewer).SignedIn();
}
