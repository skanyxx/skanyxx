using FluentValidation;

namespace Skanyxx.Module.Chat.Features.Agents;

internal sealed class ChatAgentDetailsValidator : AbstractValidator<ChatAgentDetailsQuery>
{
    public ChatAgentDetailsValidator()
    {
        RuleFor(q => q.Namespace).NotEmpty().MaximumLength(63).Matches(KubeName.Pattern);
        RuleFor(q => q.Name).NotEmpty().MaximumLength(63).Matches(KubeName.Pattern);
    }
}
