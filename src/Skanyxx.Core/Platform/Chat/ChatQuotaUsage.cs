namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// What one turn used, for the quota. <paramref name="Model"/> is the agent's ModelConfig as <c>namespace/name</c> (the
/// price key). <paramref name="Estimated"/>: kagent reported no tokens for the turn, so <paramref name="Usage"/> is Chat's
/// own estimate (characters / 4), and the quota shows it as one.
/// </summary>
public sealed record ChatQuotaUsage(string UserId, string AgentRef, string Model, string ConversationId, ChatUsage Usage, bool Estimated);
