namespace Skanyxx.Core.Platform.Memory;

/// <param name="ValueNames">The header names a value is stored for (the values are never in this record).</param>
/// <param name="ValueFingerprint">Names the stored set of values (random, new when they change); null when none are stored.</param>
/// <param name="DeployedFingerprint">Fingerprint of the values kagent was last given (D118's pattern).</param>
/// <param name="DeployedSpecHash">Hash of the server spec kagent was last given: kagent offers no read of it.</param>
/// <param name="Claimed">The studio created this server in kagent, so it may replace or delete it; it never touches one it did not.</param>
/// <param name="ValueAddress">The address (scheme, host, port and path) the values were entered for: they are sent nowhere else (D220).</param>
/// <param name="UpdatedAt">When the record last changed (UTC).</param>
public sealed record PluginServerRecord(
    string Server, string Plugin, IReadOnlyList<string> ValueNames, string? ValueFingerprint, string? DeployedFingerprint, string? DeployedSpecHash, bool Claimed,
    string? ValueAddress, DateTime UpdatedAt);
