using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Core.Platform.Plugins;
using Skanyxx.Module.Memory.Data;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>
/// Read, merge and write under the row's lock, so an install and the owner's entry never lose each other's values. A row
/// of another plugin is never taken over (D220): its values were entered for that plugin, and the studio deletes the row
/// once nothing can deploy it.
/// </summary>
internal sealed class StorePluginServerValuesHandler(MemoryDbContext db, PluginServerValueProtector protector, ILogger<StorePluginServerValuesHandler> logger)
    : IRequestHandler<StorePluginServerValuesCommand, Outcome<string>>
{
    public async Task<Outcome<string>> Handle(StorePluginServerValuesCommand command, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO memory_studio_plugin_servers (server, plugin, value_names, claimed, updated_at)
            VALUES ({command.Server}, {command.Plugin}, {Array.Empty<string>()}, false, {now})
            ON CONFLICT (server) DO NOTHING
            """, ct);
        var row = (await db.StudioPluginServers
            .FromSqlInterpolated($"SELECT * FROM memory_studio_plugin_servers WHERE server = {command.Server} FOR UPDATE").ToListAsync(ct)).Single();
        if (row.Plugin != command.Plugin)
            return Outcome<string>.Conflict(null, row.Claimed
                ? $"kagent runs the server '{command.Server}' for the plugin '{row.Plugin}'."
                : $"Skanyxx holds values for a server '{command.Server}' of the plugin '{row.Plugin}' (an open or just closed proposal); they are deleted once nothing can deploy them.",
                "taken");

        // Values entered for another address are never sent to this one, and an install (KeepExisting) never drops a
        // claimed row's: the server they belong to runs there until the move is merged. An unclaimed row runs nowhere.
        var sameAddress = row.ValueAddress == command.Address;
        if (!sameAddress && command.KeepExisting && row.Claimed && row.ValueNames.Length > 0)
            return Outcome<string>.Conflict(null,
                $"Skanyxx holds values for '{command.Server}' at {row.ValueAddress}; they stay until the move is merged, then the owner enters the new ones.", "moved");
        var before = sameAddress ? Current(row.ProtectedValues) : new(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(before, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in command.Values)
            if (!command.KeepExisting || !values.ContainsKey(name))
            {
                values.Remove(name);
                values[name] = value;
            }
        var changed = !sameAddress || !Canonical(values).SequenceEqual(Canonical(before));
        if (changed)
        {
            row.ProtectedValues = protector.Protect(values);
            row.ValueNames = [.. values.Keys.Order(StringComparer.OrdinalIgnoreCase)];
            row.ValueAddress = command.Address;
            row.ValueFingerprint = PluginHeaderFingerprint.New();
        }
        // Even unchanged: a reinstall's proposal opens after this, and the pass spares only young records (D220).
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (changed)
            logger.LogWarning("Plugin server {Server} ({Plugin}): header values stored by {Actor} for {Headers} at {Address}, fingerprint {Fingerprint}",
                command.Server, command.Plugin, command.Actor, string.Join(", ", row.ValueNames), row.ValueAddress, row.ValueFingerprint);
        return Outcome<string>.Ok(row.ValueFingerprint!);
    }

    private static IEnumerable<KeyValuePair<string, string>> Canonical(Dictionary<string, string> values) => values.OrderBy(v => v.Key, StringComparer.Ordinal);

    private Dictionary<string, string> Current(string? protectedValues)
    {
        if (protectedValues is null)
            return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            return protector.Unprotect(protectedValues);
        }
        catch (CryptographicException)
        {
            logger.LogError("Stored plugin server values cannot be decrypted (their Data Protection key is gone); they are replaced by what is entered now");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
