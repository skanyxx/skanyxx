using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

internal sealed class ListSandboxesValidator : AbstractValidator<ListSandboxesQuery>
{
    public ListSandboxesValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Limit).InclusiveBetween(1, ListSandboxesQuery.MaxLimit).OverridePropertyName("limit");
        RuleFor(q => q.Offset).InclusiveBetween(0, 1_000_000).OverridePropertyName("offset");
    }
}
