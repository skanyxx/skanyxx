using FluentValidation;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class UpsertCardValidator : AbstractValidator<UpsertCardCommand>
{
    private static readonly string[] Types = Enum.GetNames<CardType>().Select(n => n.ToLowerInvariant()).ToArray();

    public UpsertCardValidator()
    {
        RuleFor(c => c.Caller).SetValidator(new CallerValidator());
        RuleFor(c => c.Scope).Must(s => Domain.Scope.TryParse(s, out _))
            .WithMessage("Scope must be 'company' or 'personal|team|department:<id>'.");
        RuleFor(c => c.Key).Must(CardKey.IsValid)
            .WithMessage("Key must be a lowercase slug (a-z, 0-9, '-'), at most 80 characters.");
        RuleFor(c => c.Version).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Type).Must(Types.Contains).WithMessage($"Type must be one of: {string.Join(", ", Types)}.");
        RuleFor(c => c.What).NotEmpty().MaximumLength(CardLimits.What).NoNulCharacters();
        RuleFor(c => c.Why).NotEmpty().MaximumLength(CardLimits.Why).NoNulCharacters();
        RuleFor(c => c.Body).MaximumLength(CardLimits.Body).NoNulCharacters();
        RuleFor(c => c.Source).MaximumLength(CardLimits.Source).NoNulCharacters();
    }
}
