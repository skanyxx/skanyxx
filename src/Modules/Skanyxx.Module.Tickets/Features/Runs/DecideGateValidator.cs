using FluentValidation;

namespace Skanyxx.Module.Tickets.Features.Runs;

internal sealed class DecideGateValidator : AbstractValidator<DecideGateCommand>
{
    private const string Choice = "decision must be approve or reject.";

    public DecideGateValidator()
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Id).ValidRunId();
        RuleFor(c => c.Decision).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(Choice).IsInEnum().WithMessage(Choice).OverridePropertyName("decision");
        RuleFor(c => c.Note).MaximumLength(4000).NoNul().OverridePropertyName("note");
    }
}
