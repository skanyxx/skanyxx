namespace Skanyxx.Core.Platform;

/// <summary>409 body: why, plus the current state so the writer can re-read and decide.</summary>
public sealed record ConflictResponse<T>(string Message, T? Current);
