using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class SearchCardsValidator : AbstractValidator<SearchCardsQuery>
{
    public const int MaxQueryLength = CardFormat.MaxQueryLength;

    public SearchCardsValidator()
    {
        RuleFor(q => q.Caller).SetValidator(new CallerValidator());
        RuleFor(q => q.Query).NotEmpty().MaximumLength(MaxQueryLength).NoNulCharacters();
    }
}
