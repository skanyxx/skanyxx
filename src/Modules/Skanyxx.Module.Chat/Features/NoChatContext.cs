using Skanyxx.Core.Platform.ChatContext;

namespace Skanyxx.Module.Chat.Features;

/// <summary>Chat without the chatcontext module: no related work, no index, no cache.</summary>
internal sealed class NoChatContext : IChatContext
{
    public Task<ChatPreparation> PrepareAsync(ChatTurnStart start, CancellationToken ct) => Task.FromResult(ChatPreparation.None);

    public Task CompletedAsync(ChatTurnDone done, CancellationToken ct) => Task.CompletedTask;
}
