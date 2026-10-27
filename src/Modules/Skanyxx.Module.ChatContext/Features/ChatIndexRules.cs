using FluentValidation;
using Skanyxx.Core.Platform.ChatContext;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.ChatContext.Features;

internal static class ChatIndexRules
{
    public const int MaxTextLength = CardFormat.MaxQueryLength;

    /// <summary>kagent's session id shape, as Chat accepts a conversation id.</summary>
    public static IRuleBuilderOptions<T, string> SessionId<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(128).Matches("^[A-Za-z0-9_-]+$");

    public static IRuleBuilderOptions<T, string?> TicketKey<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(ChatTickets.IsValid).WithMessage("Invalid ticket key.");
}
