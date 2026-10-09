using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>Rename is a human action: the command carries a <see cref="LibraryUser"/>, so an agent cannot send one.</summary>
internal sealed class RenameCardValidator : AbstractValidator<RenameCardCommand>
{
    public RenameCardValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new LibraryUserValidator());
        RuleFor(c => c.Scope).Must(s => Scope.TryParse(s, out _)).WithMessage("Invalid scope.");
        RuleFor(c => c.Key).Must(CardKey.IsValid).WithMessage("Invalid key.");
        RuleFor(c => c.NewKey).Must(CardKey.IsValid)
            .WithMessage("Key must be a lowercase slug (a-z, 0-9, '-'), at most 80 characters.");
        RuleFor(c => c.NewKey).NotEqual(c => c.Key).When(c => CardKey.IsValid(c.NewKey)).WithMessage("The new key is the current key.");
        RuleFor(c => c.Version).GreaterThan(0);
    }
}
