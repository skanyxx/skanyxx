using FluentValidation;
using Skanyxx.Module.Tickets.Features.Pipelines;
using Skanyxx.Module.Tickets.Sources;

namespace Skanyxx.Module.Tickets.Features.Runs;

internal sealed class StartRunValidator : AbstractValidator<StartRunCommand>
{
    public StartRunValidator()
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.TicketKey).Must(TicketKey.IsValid).OverridePropertyName("ticketKey").WithMessage("Invalid ticket key.");
        RuleFor(c => c.PipelineId).Must(PipelineRules.IsSlug).OverridePropertyName("pipelineId").WithMessage("Invalid pipeline id.");
    }
}
