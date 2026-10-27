using FluentValidation;
using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class LinkTicketsValidator : AbstractValidator<LinkTicketsCommand>
{
    public LinkTicketsValidator()
    {
        RuleFor(c => c.SessionId).SessionId();
        RuleFor(c => c.TicketKeys).Must(k => k.Count <= ChatTickets.MaxPerSession).OverridePropertyName("ticketKeys");
        RuleForEach(c => c.TicketKeys).TicketKey().OverridePropertyName("ticketKeys");
    }
}
