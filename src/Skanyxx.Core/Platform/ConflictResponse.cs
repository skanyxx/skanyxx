using System.Text.Json.Serialization;

namespace Skanyxx.Core.Platform;

/// <summary>
/// 409 body: why, plus the current state so the writer can re-read and decide. <see cref="Reason"/> is a machine-readable
/// kind for requests that can conflict in more than one way (omitted when there is only one).
/// </summary>
public sealed record ConflictResponse<T>(
    string Message, T? Current, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null);
