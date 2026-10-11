using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

internal sealed class GetSandboxValidator : AbstractValidator<GetSandboxQuery>
{
    public GetSandboxValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Name).ValidName();
    }
}
