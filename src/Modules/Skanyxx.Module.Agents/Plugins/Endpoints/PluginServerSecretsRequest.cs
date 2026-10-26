namespace Skanyxx.Module.Agents.Plugins.Endpoints;

/// <summary>Header values for a plugin server: <c>{"values": {"Authorization": "Bearer …"}}</c>. Never echoed or logged.</summary>
internal sealed class PluginServerSecretsRequest
{
    public string Server { get; set; } = "";

    public Dictionary<string, string>? Values { get; set; }
}
