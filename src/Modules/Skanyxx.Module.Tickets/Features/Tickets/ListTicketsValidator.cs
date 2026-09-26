using FluentValidation;

namespace Skanyxx.Module.Tickets.Features.Tickets;

internal sealed class ListTicketsValidator : AbstractValidator<ListTicketsQuery>
{
    public ListTicketsValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Limit).InclusiveBetween(1, TicketScan.Size).OverridePropertyName("limit");
        RuleFor(q => q.Assignee).MaximumLength(200).Must(a => a is null || a.Trim().Length > 0)
            .OverridePropertyName("assignee").WithMessage("Use 'unassigned' for tickets with no owner.");
    }
}
