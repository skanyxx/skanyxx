namespace Skanyxx.Module.Chat.Endpoints;

/// <param name="TicketKeys">Jira keys this conversation is about (e.g. started on SDB-58), linked in the chat index.</param>
/// <param name="NoCache">Ask kagent even when the answer cache holds this question.</param>
internal sealed record SendChatRequest(
    string? Message, string? AgentNamespace, string? AgentName, string? ConversationId, string[]? TicketKeys = null, bool NoCache = false);
