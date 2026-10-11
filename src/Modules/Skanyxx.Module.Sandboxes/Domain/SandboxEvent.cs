using System.Text.Json.Serialization;

namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>
/// One frame of a watch: <c>initial</c> / <c>modified</c> from AX, then exactly one of <c>final</c> (a fresh read after
/// AX closed the stream), <c>gone</c> (the sandbox no longer exists) or <c>error</c> (a fixed category, never AX's text).
/// The sandbox is serialized as <c>task</c>: the frame's JSON shape predates the rename (D179) and stays as it was.
/// </summary>
public sealed record SandboxEvent(string Kind, [property: JsonPropertyName("task")] Sandbox? Sandbox = null, string? Error = null);
