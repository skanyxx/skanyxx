namespace Skanyxx.Module.ChatContext.Cache;

/// <summary>
/// Hits and estimated tokens saved (4 characters a token, question plus answer): the caller's own, and for the owner the
/// whole install's (<paramref name="AllHits"/>, <paramref name="AllTokensSaved"/>; null otherwise). An estimate only.
/// </summary>
public sealed record AnswerCacheStats(bool Enabled, long Hits, long TokensSaved, long? AllHits, long? AllTokensSaved);
