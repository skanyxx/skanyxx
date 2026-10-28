namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>kagent's session ids (A2A context ids): never a path, never a query.</summary>
internal static class ConversationId
{
    public const string Pattern = "^[A-Za-z0-9_-]+$";

    public const int MaxLength = 128;
}
