using FluentValidation;

namespace Skanyxx.Module.Chat.Features.Conversations;

internal sealed class RenameConversationValidator : AbstractValidator<RenameConversationCommand>
{
    public RenameConversationValidator()
    {
        RuleFor(c => c.ConversationId).NotEmpty().MaximumLength(ConversationId.MaxLength).Matches(ConversationId.Pattern);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(RenameConversationCommand.MaxTitle)
            .Must(t => !t.Any(char.IsControl)).WithMessage("A title is one line of text.");
    }
}
