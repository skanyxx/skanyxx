namespace Skanyxx.Module.ChatContext.Extraction;

/// <summary>
/// Folds one completed turn into a session's digest (D239). The default is <see cref="DeterministicExtractor"/> (no model
/// call); a model-based extractor registers in its place. It runs on the turn's request after the answer is back, so a
/// slow one belongs behind a queue.
/// </summary>
public interface IChatExtractor
{
    Task<SessionDigest> ExtractAsync(SessionDigest current, ChatTurn turn, CancellationToken ct);
}
