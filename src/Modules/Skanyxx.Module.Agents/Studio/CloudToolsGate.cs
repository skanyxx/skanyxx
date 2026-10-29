using Microsoft.Extensions.Configuration;
using Skanyxx.Core.Platform.Settings;

namespace Skanyxx.Module.Agents.Studio;

/// <summary>
/// The one cloud tools gate (D364, D354, D370): the licence (<see cref="CloudToolsFeature.LicenceKey"/>) AND the owner's
/// Settings switch (<c>features.cloudTools</c>), read from the live configuration on every call, so a switch saved on the
/// Settings page counts on the next request without a restart. Studio's rules, templates, form options, the merge
/// inspector and the Agents page's direct save all ask this, never the licence alone.
/// </summary>
internal sealed class CloudToolsGate(IConfiguration configuration)
{
    public bool IsOn => CloudToolsFeature.IsOn(configuration);
}
