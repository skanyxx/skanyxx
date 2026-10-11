using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

internal sealed class SetSandboxSuspendedValidator : AbstractValidator<SetSandboxSuspendedCommand>
{
    public SetSandboxSuspendedValidator()
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Name).ValidName();
    }
}
