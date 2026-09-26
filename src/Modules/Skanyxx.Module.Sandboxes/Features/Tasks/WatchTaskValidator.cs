using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

internal sealed class WatchTaskValidator : AbstractValidator<WatchTaskQuery>
{
    public WatchTaskValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Name).ValidName();
    }
}
