using FluentValidation;

namespace Skanyxx.Module.Tickets.Features.Tickets;

internal sealed class ListAssigneesValidator : AbstractValidator<ListAssigneesQuery>
{
    public ListAssigneesValidator() => RuleFor(q => q.UserId).ValidUser();
}
