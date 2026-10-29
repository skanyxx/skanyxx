using FluentValidation;

namespace Skanyxx.Module.Investigate.Features.Runs;

internal sealed class CancelRunValidator : AbstractValidator<CancelRunCommand>
{
    public CancelRunValidator() => RuleFor(c => c.Viewer).SignedIn();
}
