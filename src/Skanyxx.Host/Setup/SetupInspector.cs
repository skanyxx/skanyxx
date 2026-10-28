using Microsoft.Extensions.Configuration;

namespace Skanyxx.Host.Setup;

/// <summary>Reads the configuration server-side and answers which of a guide's settings are set.</summary>
public sealed class SetupInspector(IConfiguration configuration)
{
    public SetupState Inspect(SetupGuide guide)
    {
        var set = guide.Settings.Where(s => s.IsSetIn(configuration)).ToList();
        var unset = guide.Settings.Except(set).ToList();
        return new([.. unset.Where(s => !s.Optional)], [.. unset.Where(s => s.Optional)], set);
    }
}
