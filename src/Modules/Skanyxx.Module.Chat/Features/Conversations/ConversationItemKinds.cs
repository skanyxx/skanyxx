namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>What a conversation shows: the person's messages and the answers, no tool steps (D555).</summary>
internal static class ConversationItemKinds
{
    public const string User = "user";

    /// <summary>The turn's answer.</summary>
    public const string Assistant = "assistant";
}
