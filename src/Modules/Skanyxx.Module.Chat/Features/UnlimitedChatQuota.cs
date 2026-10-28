using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Module.Chat.Features;

/// <summary>No chat limits until a quota module replaces this (the <see cref="IChatQuota"/> hook).</summary>
internal sealed class UnlimitedChatQuota : IChatQuota
{
    public Task<string?> RefusalAsync(string userId, string agentRef, CancellationToken ct) => Task.FromResult<string?>(null);

    public Task RecordAsync(string userId, string agentRef, string conversationId, ChatUsage usage, CancellationToken ct) => Task.CompletedTask;
}
