using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

internal sealed class StopTaskValidator : AbstractValidator<StopTaskCommand>
{
    public StopTaskValidator()
    {
        RuleFor(c => c.UserId).ValidUser();
        RuleFor(c => c.Name).ValidName();
    }
}
