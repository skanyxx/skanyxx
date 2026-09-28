using FluentValidation;

namespace Skanyxx.Module.Identity.Features.Me;

internal sealed class GetMeValidator : AbstractValidator<GetMeQuery>
{
    public GetMeValidator() => RuleFor(q => q.UserId).NotEmpty();
}
