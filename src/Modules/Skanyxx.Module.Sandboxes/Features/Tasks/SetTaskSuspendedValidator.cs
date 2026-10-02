using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

internal sealed class SetTaskSuspendedValidator : AbstractValidator<SetTaskSuspendedCommand>
{
    public SetTaskSuspendedValidator()
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Name).ValidName();
    }
}
