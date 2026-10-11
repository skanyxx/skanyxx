using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

internal sealed class WatchSandboxValidator : AbstractValidator<WatchSandboxQuery>
{
    public WatchSandboxValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Name).ValidName();
    }
}
