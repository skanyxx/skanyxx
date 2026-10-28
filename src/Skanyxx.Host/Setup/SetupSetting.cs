using Microsoft.Extensions.Configuration;

namespace Skanyxx.Host.Setup;

/// <summary>
/// A configuration key a feature reads. Guides name it, say in plain words what does not work while it is unset, and
/// where its value comes from; only whether it is set is ever read (<see cref="IsSetIn"/>), never the value itself,
/// since many of them are secrets.
/// </summary>
/// <param name="Effect">What does not work while it is unset, in plain words.</param>
/// <param name="Alternative">Another key that does the same job (e.g. <c>KAgent:IngressUrl</c> beside <c>KAgent:BaseUrl</c>).</param>
/// <param name="MustBeTrue">A switch: set means <c>true</c>.</param>
public sealed record SetupSetting(
    string Key, string Effect, string WhereToGet, bool Optional = false, bool MustBeTrue = false, string? Alternative = null)
{
    public string Name => Alternative is null ? Key : $"{Key} (or {Alternative})";

    public bool IsSetIn(IConfiguration configuration) =>
        Has(configuration, Key) || (Alternative is not null && Has(configuration, Alternative));

    private bool Has(IConfiguration configuration, string key)
    {
        if (MustBeTrue)
            return bool.TryParse(configuration[key], out var on) && on;
        // A list (Sandboxes:AllowedImages, Plugins:GitHosts) has no value of its own, only children.
        var section = configuration.GetSection(key);
        return !string.IsNullOrWhiteSpace(section.Value) || section.GetChildren().Any();
    }
}
