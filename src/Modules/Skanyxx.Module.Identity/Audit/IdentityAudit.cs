using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Identity.Accounts;
using Skanyxx.Module.Identity.Data;

namespace Skanyxx.Module.Identity.Audit;

/// <summary>
/// Writes audit rows (D152) with one INSERT on the request's own connection, so a row written inside a handler's
/// transaction commits or rolls back with the change it describes: a failed action leaves no row. Outside a
/// transaction (a refusal, which changes nothing) the INSERT stands alone. The client address is the connection's, as
/// in the Warning lines; a background job has none and passes its own (or null).
/// </summary>
internal sealed class IdentityAudit(AccountsDbContext db, ClientAddress client, AuditSampler sampler, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task WriteAsync(string action, string? actorId, string? targetId, object? details, CancellationToken ct, string? remoteIp = null)
    {
        var json = details is null ? null : JsonSerializer.Serialize(details, Json);
        var ip = remoteIp ?? client.Current?.ToString();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity_audit ("AtUtc", "Action", "ActorId", "TargetId", "RemoteIp", "Details")
            VALUES ({time.GetUtcNow()}, {action}, {actorId}, {targetId}, {ip}, CAST({json} AS jsonb))
            """, ct);
    }

    /// <summary>
    /// For refusals anyone can cause (D167): written only when <see cref="AuditSampler"/> admits this
    /// <paramref name="action"/> + <paramref name="kind"/>, with <c>skippedBefore</c> — how many of them were not written
    /// since the previous row — added to the details.
    /// </summary>
    public Task WriteSampledAsync(string action, string kind, string? actorId, string? targetId, object? details, CancellationToken ct, string? remoteIp = null)
    {
        if (sampler.Admit(action + "|" + kind) is not { } skipped)
            return Task.CompletedTask;
        var json = details is null ? new JsonObject() : (JsonObject)JsonSerializer.SerializeToNode(details, Json)!;
        json["skippedBefore"] = skipped;
        return WriteAsync(action, actorId, targetId, json, ct, remoteIp);
    }
}
