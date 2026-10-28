namespace Skanyxx.Core.Platform.Memory;

/// <summary>What a new conversation is about: the person, the agent they talk to (its memory id) and the first message.</summary>
/// <param name="TicketKeys">Tickets the conversation is known to be about, besides any the message mentions.</param>
public sealed record RelatedMemoryQuery(string UserId, string AgentId, string Text, IReadOnlyList<string>? TicketKeys = null);
