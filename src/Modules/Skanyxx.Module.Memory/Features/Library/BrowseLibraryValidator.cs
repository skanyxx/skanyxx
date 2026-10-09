using FluentValidation;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Domain;
using Skanyxx.Module.Memory.Features.Cards;

namespace Skanyxx.Module.Memory.Features.Library;

internal sealed class BrowseLibraryValidator : AbstractValidator<BrowseLibraryQuery>
{
    public BrowseLibraryValidator()
    {
        RuleFor(q => q.User).NotNull().SetValidator(new LibraryUserValidator());
        RuleFor(q => q.Text).MaximumLength(CardFormat.MaxQueryLength).NoNulCharacters();
        RuleFor(q => q.Scope).Must(s => Scope.TryParse(s, out _)).When(q => !string.IsNullOrEmpty(q.Scope)).WithMessage("Invalid scope.");
    }
}
