using FluentValidation;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

internal sealed class DeletePipelineValidator : AbstractValidator<DeletePipelineCommand>
{
    public DeletePipelineValidator()
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Id).Must(PipelineRules.IsSlug).OverridePropertyName("id").WithMessage("Invalid pipeline id.");
    }
}
