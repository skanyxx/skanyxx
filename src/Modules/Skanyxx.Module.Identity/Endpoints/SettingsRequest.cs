namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Setting key → value, for the keys being changed (D350). Unknown keys are a 400.</summary>
public sealed class SettingsRequest
{
    public Dictionary<string, string> Values { get; set; } = [];
}
