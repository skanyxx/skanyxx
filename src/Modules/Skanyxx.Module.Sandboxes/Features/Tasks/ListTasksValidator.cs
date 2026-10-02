using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

internal sealed class ListTasksValidator : AbstractValidator<ListTasksQuery>
{
    public ListTasksValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Limit).InclusiveBetween(1, ListTasksQuery.MaxLimit).OverridePropertyName("limit");
        RuleFor(q => q.Offset).InclusiveBetween(0, 1_000_000).OverridePropertyName("offset");
    }
}
