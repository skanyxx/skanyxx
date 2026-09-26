using FluentValidation;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class LiftCardValidator : AbstractValidator<LiftCardCommand>
{
    public LiftCardValidator()
    {
        RuleFor(c => c.Caller).SetValidator(new CallerValidator());
        RuleFor(c => c.Caller.AgentId).Null().WithName("X-Agent-Id").WithMessage("Lift is a human action.");
        RuleFor(c => c.FromScope).Must(s => Scope.TryParse(s, out _)).WithMessage("Invalid scope.");
        RuleFor(c => c.ToScope).Must(s => Scope.TryParse(s, out _)).WithMessage("Invalid target scope.");
        RuleFor(c => c.Key).Must(CardKey.IsValid).WithMessage("Invalid key.");
        RuleFor(c => c)
            .Must(c => Scope.Parse(c.ToScope).Level > Scope.Parse(c.FromScope).Level)
            .When(c => Scope.TryParse(c.FromScope, out _) && Scope.TryParse(c.ToScope, out _))
            .OverridePropertyName("targetScope") // bypasses the camelCase resolver, so spelled as sent
            .WithMessage("A lift must go to a higher scope (personal → team → department → company).");
    }
}
