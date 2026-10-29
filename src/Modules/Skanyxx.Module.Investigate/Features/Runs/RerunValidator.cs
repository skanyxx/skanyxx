using FluentValidation;

namespace Skanyxx.Module.Investigate.Features.Runs;

internal sealed class RerunValidator : AbstractValidator<RerunCommand>
{
    public RerunValidator() => RuleFor(c => c.Viewer).SignedIn();
}
