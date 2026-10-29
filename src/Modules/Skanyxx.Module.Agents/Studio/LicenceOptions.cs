namespace Skanyxx.Module.Agents.Studio;

/// <summary>
/// <c>Skanyxx:Licence</c>: paid features (D364). Read through <c>IOptions</c> only; a restart applies a change.
/// </summary>
public sealed class LicenceOptions
{
    public const string Section = "Skanyxx:Licence";

    /// <summary>Tools that call cloud provider APIs (<see cref="StudioOptions.CloudToolServers"/>) may be added to studio agents.</summary>
    public bool CloudTools { get; set; }
}
