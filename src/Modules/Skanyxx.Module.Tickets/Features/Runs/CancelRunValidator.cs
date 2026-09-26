using FluentValidation;

namespace Skanyxx.Module.Tickets.Features.Runs;

internal sealed class CancelRunValidator : AbstractValidator<CancelRunCommand>
{
    public CancelRunValidator()
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Id).ValidRunId();
    }
}
