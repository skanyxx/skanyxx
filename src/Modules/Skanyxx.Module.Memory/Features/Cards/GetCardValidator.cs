using FluentValidation;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class GetCardValidator : AbstractValidator<GetCardQuery>
{
    public GetCardValidator()
    {
        RuleFor(q => q.Caller).SetValidator(new CallerValidator());
        RuleFor(q => q.Scope).Must(s => Domain.Scope.TryParse(s, out _)).WithMessage("Invalid scope.");
        RuleFor(q => q.Key).Must(CardKey.IsValid).WithMessage("Invalid key.");
    }
}
