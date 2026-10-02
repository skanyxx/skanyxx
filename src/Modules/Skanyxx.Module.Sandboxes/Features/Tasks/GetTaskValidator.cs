using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

internal sealed class GetTaskValidator : AbstractValidator<GetTaskQuery>
{
    public GetTaskValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Name).ValidName();
    }
}
