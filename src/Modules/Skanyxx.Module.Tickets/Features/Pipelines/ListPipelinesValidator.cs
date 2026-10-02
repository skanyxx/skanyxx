using FluentValidation;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

internal sealed class ListPipelinesValidator : AbstractValidator<ListPipelinesQuery>
{
    public ListPipelinesValidator() => RuleFor(q => q.UserId).ValidUser();
}
