using FluentValidation;

namespace Skanyxx.Module.Investigate.Features.Workflows;

internal sealed class ListWorkflowsValidator : AbstractValidator<ListWorkflowsQuery>
{
    public ListWorkflowsValidator() => RuleFor(q => q.Viewer).SignedIn();
}
