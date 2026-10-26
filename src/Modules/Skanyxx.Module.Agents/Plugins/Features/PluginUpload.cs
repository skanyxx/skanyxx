namespace Skanyxx.Module.Agents.Plugins.Features;

/// <summary>An uploaded zip: its file name (the plugin's fallback name and its source location) and its bytes.</summary>
internal sealed record PluginUpload(string FileName, byte[] Bytes)
{
    public override string ToString() => $"PluginUpload {{ FileName = {FileName}, Bytes = {Bytes.Length} }}";
}
