using FluentValidation;
using Skanyxx.Module.Tickets.Sources;

namespace Skanyxx.Module.Tickets.Features.Runs;

internal sealed class ListRunsValidator : AbstractValidator<ListRunsQuery>
{
    public ListRunsValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.TicketKey).Must(TicketKey.IsValid).When(q => q.TicketKey is not null)
            .OverridePropertyName("ticketKey").WithMessage("Invalid ticket key.");
    }
}
