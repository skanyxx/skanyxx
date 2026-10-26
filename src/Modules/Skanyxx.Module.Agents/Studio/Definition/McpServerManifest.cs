using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// A plugin's remote MCP server, <c>plugins/&lt;p&gt;/mcp/&lt;server&gt;.yaml</c>: a kagent <c>v1alpha2</c> RemoteMCPServer
/// exactly as the reconciler applies it, except labels and the Secret's name (the reconciler suffixes it with the values'
/// fingerprint). Every header comes from that Secret, under the lowercased header name: git holds names, never values.
/// Reading back is strict like <see cref="AgentManifest"/>: an inline header value, a ConfigMap reference, TLS settings,
/// allowed namespaces or any other field make the file invalid.
/// </summary>
internal static class McpServerManifest
{
    private const string Header = "# Skanyxx plugin server. Applied to kagent by Skanyxx's reconciler when a supervisor merges; header values come from a Secret, never from git.\n";

    public static string Render(PluginServerDraft server, string ns) => Header + YamlTree.Write(Server(server, ns));

    /// <summary>The RemoteMCPServer as git holds it (Secret name = server name), without labels.</summary>
    public static JsonObject Server(PluginServerDraft server, string ns)
    {
        var spec = new JsonObject
        {
            ["description"] = server.Description,
            ["protocol"] = server.Protocol,
            ["url"] = server.Url
        };
        if (server.Headers.Count > 0)
            spec["headersFrom"] = new JsonArray([.. server.Headers.Select(h => (JsonNode)new JsonObject
            {
                ["name"] = h.Name,
                ["valueFrom"] = new JsonObject { ["type"] = "Secret", ["name"] = server.Name, ["key"] = SecretKey(h.Name) }
            })]);
        return new JsonObject
        {
            ["apiVersion"] = "kagent.dev/v1alpha2",
            ["kind"] = "RemoteMCPServer",
            ["metadata"] = new JsonObject { ["name"] = server.Name, ["namespace"] = ns },
            ["spec"] = spec
        };
    }

    /// <summary>The key of a header's value in the server's Secret.</summary>
    public static string SecretKey(string header) => header.ToLowerInvariant();

    /// <summary>What kagent was last given is compared through this (kagent has no read of a server's spec).</summary>
    public static string SpecHash(PluginServerDraft server, string ns) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Server(server, ns).ToJsonString())))[..16];

    /// <summary>The server <paramref name="yaml"/> describes (headers by name only), or the reason it is not one of ours.</summary>
    public static (PluginServerDraft? Server, string? Problem) Read(string fileName, string yaml, string ns)
    {
        JsonNode? tree;
        try
        {
            tree = YamlTree.Read(yaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            return (null, $"Not valid YAML: {ex.Message.Split('\n')[0]}");
        }
        if (tree is not JsonObject root)
            return (null, "A plugin server file must be a YAML mapping.");

        var spec = At(root, "spec");
        var server = new PluginServerDraft(
            Text(At(At(root, "metadata"), "name")) ?? "",
            Text(At(spec, "description")) ?? "",
            Text(At(spec, "protocol")) ?? "",
            Text(At(spec, "url")) ?? "",
            [.. Items(At(spec, "headersFrom")).Select(h => new PluginHeader(Text(At(h, "name")) ?? "", null, null))]);
        if (server.Name != fileName)
            return (null, $"The server file {fileName}.yaml must name the server '{fileName}'.");
        if (!JsonNode.DeepEquals(YamlTree.AsRead(Server(server, ns)), root))
            return (null, $"{fileName}.yaml is not a plugin server: only a kagent RemoteMCPServer with a description, protocol, url and headers taken "
                + "from its own Secret, in this namespace, may be merged (no inline header values, no ConfigMaps, no TLS settings).");
        return (server, null);
    }

    private static JsonNode? At(JsonNode? node, string key) => node is JsonObject o ? o[key] : null;

    private static IEnumerable<JsonNode?> Items(JsonNode? node) => node as JsonArray ?? [];

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
