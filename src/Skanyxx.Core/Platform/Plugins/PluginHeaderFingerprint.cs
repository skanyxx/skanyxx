using System.Security.Cryptography;

namespace Skanyxx.Core.Platform.Plugins;

/// <summary>
/// The name of one stored set of header values (D118's pattern): 12 random hex, new whenever the values change. It names
/// the server's Secret in kagent and is derived from nothing, so it tells nothing about the values (a hash of them would
/// confirm a guess, D220).
/// </summary>
public static class PluginHeaderFingerprint
{
    /// <summary>The fingerprint of a server that is sent no header.</summary>
    public const string None = "000000000000";

    public static string New() => RandomNumberGenerator.GetHexString(12, lowercase: true);
}
