namespace Skanyxx.Module.Chat.Features.Files;

/// <summary>
/// One uploaded file's record, next to its bytes. <paramref name="ConversationId"/> is set when it is sent with a turn;
/// until then it belongs to no conversation. <paramref name="Kind"/> is <see cref="ChatFileKinds"/>.
/// </summary>
internal sealed record StoredChatFile(
    string Id, string Name, string Kind, string MediaType, long Size, bool SecretsFound, string? ConversationId, DateTimeOffset CreatedAt);
