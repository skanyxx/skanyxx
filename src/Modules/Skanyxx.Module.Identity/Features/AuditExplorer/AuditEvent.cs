using System.Text.Json;
using Skanyxx.Module.Identity.Audit;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>One audit row as the explorer shows it; <paramref name="Details"/> redacted again on the way out.</summary>
internal sealed record AuditEvent(
    long Id, DateTimeOffset At, string Action, string Module, string Outcome, string? ActorId, string? TargetId, string? RemoteIp, JsonElement? Details)
{
    public static AuditEvent From(AuditEntry entry) =>
        new(entry.Id, entry.AtUtc, entry.Action, entry.Module, entry.Outcome, entry.ActorId, entry.TargetId, entry.RemoteIp,
            AuditRedactor.Redact(entry.Details) is { } json ? JsonSerializer.Deserialize<JsonElement>(json) : null);
}
