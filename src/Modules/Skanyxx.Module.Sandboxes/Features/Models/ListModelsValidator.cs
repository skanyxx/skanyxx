using FluentValidation;

namespace Skanyxx.Module.Sandboxes.Features.Models;

internal sealed class ListModelsValidator : AbstractValidator<ListModelsQuery>
{
    public ListModelsValidator() => RuleFor(q => q.UserId).ValidUser();
}
