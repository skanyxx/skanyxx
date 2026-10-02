namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>
/// One frame of a watch: <c>initial</c> / <c>modified</c> from AX, then exactly one of <c>final</c> (a fresh read after
/// AX closed the stream), <c>gone</c> (the task no longer exists) or <c>error</c> (a fixed category, never AX's text).
/// </summary>
public sealed record SandboxTaskEvent(string Kind, SandboxTask? Task = null, string? Error = null);
