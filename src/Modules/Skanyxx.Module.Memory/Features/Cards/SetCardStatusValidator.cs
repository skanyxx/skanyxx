using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>A human action: the command carries a <see cref="LibraryUser"/>, so an agent cannot send one.</summary>
internal sealed class SetCardStatusValidator : AbstractValidator<SetCardStatusCommand>
{
    private static readonly string[] Statuses = Enum.GetNames<CardStatus>().Select(n => n.ToLowerInvariant()).ToArray();

    public SetCardStatusValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new LibraryUserValidator());
        RuleFor(c => c.Scope).Must(s => Scope.TryParse(s, out _)).WithMessage("Invalid scope.");
        RuleFor(c => c.Key).Must(CardKey.IsValid).WithMessage("Invalid key.");
        RuleFor(c => c.Version).GreaterThan(0);
        RuleFor(c => c.Status).Must(Statuses.Contains).WithMessage($"Status must be one of: {string.Join(", ", Statuses)}.");
        RuleFor(c => c.SupersededBy).Must(l => CardLinks.IsCard(l!)).When(c => c.SupersededBy is not null)
            .WithMessage("supersededBy must be a card, 'scope/key'.");
        RuleFor(c => c.SupersededBy).Null().When(c => c.Status != "stale").WithMessage("Only a stale card is superseded.");
    }
}
