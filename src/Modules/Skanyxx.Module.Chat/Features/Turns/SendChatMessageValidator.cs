using FluentValidation;
using Skanyxx.Core.MediatR.Requests;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Module.Chat.Features.Agents;
using Skanyxx.Module.Chat.Features.Conversations;
using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Module.Chat.Features.Turns;

internal sealed class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public const int MaxMessageChars = 16_000;

    public SendChatMessageValidator(ChatFiles files)
    {
        RuleFor(c => c.UserId).NotEmpty();
        // A message may be only its files.
        RuleFor(c => c.Message).NotEmpty().When(c => c.FileIds is not { Count: > 0 });
        RuleFor(c => c.Message).MaximumLength(MaxMessageChars);
        RuleFor(c => c.AgentNamespace).NotEmpty().MaximumLength(63).Matches(KubeName.Pattern);
        RuleFor(c => c.AgentName).NotEmpty().MaximumLength(63).Matches(KubeName.Pattern);
        RuleFor(c => c.TicketKeys).Must(k => k!.Count <= ChatTickets.MaxPerSession).When(c => c.TicketKeys is not null);
        RuleForEach(c => c.TicketKeys).Must(ChatTickets.IsValid).WithMessage("Invalid ticket key.");
        RuleFor(c => c.ConversationId).MaximumLength(ConversationId.MaxLength).Matches(ConversationId.Pattern).When(c => !string.IsNullOrEmpty(c.ConversationId));
        RuleFor(c => c.FileIds).Must(f => f!.Count <= files.Options.MaxFilesPerMessage)
            .WithMessage($"Attach at most {files.Options.MaxFilesPerMessage} files to one message.").When(c => c.FileIds is not null);
        RuleForEach(c => c.FileIds).Must(ChatFiles.IsId).WithMessage("Invalid file id.");
    }
}
