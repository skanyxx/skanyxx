using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>Lift is a human action: the command carries a <see cref="LibraryUser"/>, so an agent cannot send one.</summary>
internal sealed class LiftCardValidator : AbstractValidator<LiftCardCommand>
{
    public LiftCardValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new LibraryUserValidator());
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
