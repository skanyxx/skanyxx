using FluentValidation;

namespace Skanyxx.Module.Chat.Features.Conversations;

internal sealed class DeleteConversationValidator : AbstractValidator<DeleteConversationCommand>
{
    public DeleteConversationValidator() =>
        RuleFor(c => c.ConversationId).NotEmpty().MaximumLength(ConversationId.MaxLength).Matches(ConversationId.Pattern);
}
