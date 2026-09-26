using FluentValidation;
using Skanyxx.Module.Tickets.Sources;

namespace Skanyxx.Module.Tickets.Features.Tickets;

internal sealed class GetTicketValidator : AbstractValidator<GetTicketQuery>
{
    public GetTicketValidator()
    {
        RuleFor(q => q.UserId).ValidUser();
        RuleFor(q => q.Key).Must(TicketKey.IsValid).OverridePropertyName("key").WithMessage("Invalid ticket key.");
    }
}
