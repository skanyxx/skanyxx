using System.Text.Json;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>One identity audit row. <paramref name="Details"/> never holds a token, password or secret.</summary>
public sealed record AuditEntryDto(
    long Id, DateTimeOffset At, string Action, string? ActorId, string? TargetId, string? RemoteIp, JsonElement? Details);
