using FluentValidation;

namespace Skanyxx.Module.ChatContext.Features;

internal sealed class SearchSessionsValidator : AbstractValidator<SearchSessionsQuery>
{
    public SearchSessionsValidator()
    {
        RuleFor(q => q.Filter.Text).MaximumLength(ChatIndexRules.MaxTextLength).OverridePropertyName("q");
        RuleFor(q => q.Filter.Ticket).TicketKey().When(q => q.Filter.Ticket is not null).OverridePropertyName("ticket");
        RuleFor(q => q.Filter.Category).MaximumLength(40).Matches("^[a-z0-9-]+$").When(q => q.Filter.Category is not null).OverridePropertyName("category");
        RuleFor(q => q.Filter.Agent).MaximumLength(127).Matches("^[a-z0-9-]+/[a-z0-9-]+$").When(q => q.Filter.Agent is not null).OverridePropertyName("agent");
    }
}
