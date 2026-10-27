using FluentValidation;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class TicketViewValidator : AbstractValidator<TicketViewQuery>
{
    public TicketViewValidator()
    {
        RuleFor(q => q.TicketKey).TicketKey().OverridePropertyName("key");
    }
}
