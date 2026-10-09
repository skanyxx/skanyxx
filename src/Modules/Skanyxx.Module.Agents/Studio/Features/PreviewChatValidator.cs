using FluentValidation;
using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Features;

internal sealed class PreviewChatValidator : AbstractValidator<PreviewChatCommand>
{
    public const int MaxMessageChars = 16_000;

    public PreviewChatValidator()
    {
        RuleFor(c => c.User).NotNull().SetValidator(new StudioUserValidator());
        RuleFor(c => c.Number).InclusiveBetween(1, 999_999);
        RuleFor(c => c.Message).NotEmpty().MaximumLength(MaxMessageChars);
        RuleFor(c => c.ConversationId).MaximumLength(128).Matches("^[A-Za-z0-9_-]+$").When(c => !string.IsNullOrEmpty(c.ConversationId));
    }
}
