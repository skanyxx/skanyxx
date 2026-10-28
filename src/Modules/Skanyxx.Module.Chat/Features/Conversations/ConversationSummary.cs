namespace Skanyxx.Module.Chat.Features.Conversations;

/// <summary>One row of the sessions sidebar; times as kagent gives them (ISO 8601).</summary>
internal sealed record ConversationSummary(
    string Id, string Title, string AgentNamespace, string AgentName, string? CreatedAt, string? UpdatedAt);
