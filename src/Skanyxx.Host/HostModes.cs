namespace Skanyxx.Host;

/// <summary>Reads <see cref="HostMode"/> from configuration.</summary>
public static class HostModes
{
    public const string ModeKey = "Skanyxx:Mode";

    /// <summary>Absent or empty is <see cref="HostMode.Server"/>, so every config written before modes existed keeps working.</summary>
    public static HostMode Read(IConfiguration configuration) => configuration[ModeKey] switch
    {
        null or "" => HostMode.Server,
        var value when value.Equals(nameof(HostMode.Server), StringComparison.OrdinalIgnoreCase) => HostMode.Server,
        var value when value.Equals(nameof(HostMode.Remote), StringComparison.OrdinalIgnoreCase) => HostMode.Remote,
        var value => throw new InvalidOperationException($"{ModeKey} must be Server or Remote, not \"{value}\".")
    };
}
