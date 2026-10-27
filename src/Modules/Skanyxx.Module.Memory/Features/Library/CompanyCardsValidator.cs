using FluentValidation;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory.Features.Library;

internal sealed class CompanyCardsValidator : AbstractValidator<CompanyCardsQuery>
{
    public CompanyCardsValidator()
    {
        RuleFor(q => q.Text).MaximumLength(CardFormat.MaxQueryLength);
        RuleFor(q => q.Limit).InclusiveBetween(1, CompanyCardsQuery.MaxLimit);
    }
}
