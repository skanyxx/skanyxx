using FluentValidation;

namespace Skanyxx.Module.Chat.Features.Conversations;

internal sealed class GetConversationValidator : AbstractValidator<GetConversationQuery>
{
    public GetConversationValidator() =>
        RuleFor(q => q.ConversationId).NotEmpty().MaximumLength(ConversationId.MaxLength).Matches(ConversationId.Pattern);
}
