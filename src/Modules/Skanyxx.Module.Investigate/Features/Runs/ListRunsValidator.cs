using FluentValidation;

namespace Skanyxx.Module.Investigate.Features.Runs;

internal sealed class ListRunsValidator : AbstractValidator<ListRunsQuery>
{
    public ListRunsValidator()
    {
        RuleFor(q => q.Viewer).SignedIn();
        RuleFor(q => q.Limit).InclusiveBetween(1, 200).OverridePropertyName("limit");
        RuleFor(q => q.Search).MaximumLength(120).OverridePropertyName("q");
    }
}
