using FluentValidation;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

internal sealed class GetPipelineValidator : AbstractValidator<GetPipelineQuery>
{
    public GetPipelineValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Id).Must(PipelineRules.IsSlug).OverridePropertyName("id").WithMessage("Invalid pipeline id.");
    }
}
