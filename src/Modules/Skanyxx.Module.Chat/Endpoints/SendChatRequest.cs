namespace Skanyxx.Module.Chat.Endpoints;

internal sealed record SendChatRequest(string? Message, string? AgentNamespace, string? AgentName, string? ConversationId);
