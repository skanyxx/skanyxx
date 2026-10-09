using FluentValidation;
using Skanyxx.Core.MediatR.Requests;

namespace Skanyxx.Module.Chat.Features;

internal sealed class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public const int MaxMessageChars = 16_000;

    public SendChatMessageValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Message).NotEmpty().MaximumLength(MaxMessageChars);
        // Kubernetes namespace and object name (RFC 1123 labels).
        RuleFor(c => c.AgentNamespace).NotEmpty().MaximumLength(63).Matches("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$");
        RuleFor(c => c.AgentName).NotEmpty().MaximumLength(63).Matches("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$");
        RuleFor(c => c.ConversationId).MaximumLength(128).Matches("^[A-Za-z0-9_-]+$").When(c => !string.IsNullOrEmpty(c.ConversationId));
    }
}
