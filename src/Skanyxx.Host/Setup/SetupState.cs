namespace Skanyxx.Host.Setup;

/// <summary>
/// What this install's configuration says about a guide's settings. It carries the guide's own text about each
/// setting (name, effect, where to get it), never a configured value.
/// </summary>
public sealed record SetupState(IReadOnlyList<SetupSetting> Missing, IReadOnlyList<SetupSetting> OptionalUnset, IReadOnlyList<SetupSetting> Set)
{
    public bool Ready => Missing.Count == 0;
}
