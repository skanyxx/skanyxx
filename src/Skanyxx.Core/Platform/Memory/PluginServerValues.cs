using System.Text;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// Header name → value, the <see cref="Fingerprint"/> naming that set and the <see cref="Address"/> it was entered for.
/// Never printed: <see cref="ToString"/> names the headers only.
/// </summary>
public sealed record PluginServerValues(string Server, IReadOnlyDictionary<string, string> Values, string? Fingerprint, string? Address)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Server = {Server}, Values = [{string.Join(", ", Values.Keys.Select(k => $"{k} = ***"))}], Fingerprint = {Fingerprint}, Address = {Address}");
        return true;
    }
}
