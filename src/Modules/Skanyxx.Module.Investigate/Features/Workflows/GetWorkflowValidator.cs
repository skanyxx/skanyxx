using FluentValidation;

namespace Skanyxx.Module.Investigate.Features.Workflows;

internal sealed class GetWorkflowValidator : AbstractValidator<GetWorkflowQuery>
{
    public GetWorkflowValidator() => RuleFor(q => q.Viewer).SignedIn();
}
