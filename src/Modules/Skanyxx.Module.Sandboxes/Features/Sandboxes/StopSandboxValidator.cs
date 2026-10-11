using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

internal sealed class StopSandboxValidator : AbstractValidator<StopSandboxCommand>
{
    public StopSandboxValidator()
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Name).ValidName();
    }
}
