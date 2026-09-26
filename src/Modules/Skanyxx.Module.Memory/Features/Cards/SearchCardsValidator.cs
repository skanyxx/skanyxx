using FluentValidation;

namespace Skanyxx.Module.Memory.Features.Cards;

internal sealed class SearchCardsValidator : AbstractValidator<SearchCardsQuery>
{
    /// <summary>Also bounds the number of words CardSearch turns into tsqueries.</summary>
    public const int MaxQueryLength = 500;

    public SearchCardsValidator()
    {
        RuleFor(q => q.Caller).SetValidator(new CallerValidator());
        RuleFor(q => q.Query).NotEmpty().MaximumLength(MaxQueryLength).NoNulCharacters();
    }
}
