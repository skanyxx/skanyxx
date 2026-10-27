using StackExchange.Redis;

namespace Skanyxx.Module.Memory.Coordination;

internal static class DragonflyConfiguration
{
    /// <summary>Checked at startup, so a typo stops the host instead of turning every request into a fallback.</summary>
    public static bool IsValid(string configuration)
    {
        try
        {
            return ConfigurationOptions.Parse(configuration).EndPoints.Count > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
