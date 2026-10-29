using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Cards;

/// <summary>A human action: the command carries a <see cref="LibraryUser"/>, so an agent cannot send one.</summary>
internal sealed class DeleteCardValidator : AbstractValidator<DeleteCardCommand>
{
    public DeleteCardValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new LibraryUserValidator());
        RuleFor(c => c.Scope).Must(s => Scope.TryParse(s, out _)).WithMessage("Invalid scope.");
        RuleFor(c => c.Key).Must(CardKey.IsValid).WithMessage("Invalid key.");
        RuleFor(c => c.Version).GreaterThan(0);
    }
}
