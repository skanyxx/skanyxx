using System.Collections.Concurrent;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Investigate.Tests.Infrastructure;

/// <summary>Memory's card extractor for tests: records each finished investigation and answers with its card.</summary>
public sealed class FakeCards : ICardExtractor
{
    public ConcurrentQueue<InvestigationFinished> Investigations { get; } = new();

    public Task<MemoryCardRef?> ChatTurnAsync(ChatTurnDigest turn, CancellationToken ct) => Task.FromResult<MemoryCardRef?>(null);

    public Task<MemoryCardRef?> TicketRunAsync(TicketRunFinished run, CancellationToken ct) => Task.FromResult<MemoryCardRef?>(null);

    public Task<MemoryCardRef?> InvestigationAsync(InvestigationFinished run, CancellationToken ct)
    {
        Investigations.Enqueue(run);
        return Task.FromResult<MemoryCardRef?>(new($"personal:{run.OwnerUserId}", $"investigation-{run.RunId:N}", "what"));
    }
}
