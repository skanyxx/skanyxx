namespace Skanyxx.Core.Platform.Plugins;

/// <summary>
/// A header the server is sent with. <see cref="Template"/> is the plugin's value when it names variables
/// (<c>Bearer ${GITHUB_TOKEN}</c>): it tells the owner what to enter and may go to git. A literal value is never a
/// template; it is <see cref="Value"/>, stored like a secret and never written to git.
/// </summary>
public sealed record PluginHeader(string Name, string? Template, string? Value)
{
    public override string ToString() => $"PluginHeader {{ Name = {Name}, Template = {Template}, Value = {(Value is null ? "null" : "***")} }}";
}
