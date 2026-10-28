using System.Text.Json.Nodes;
using Skanyxx.Module.ToolServers.Data;

namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>The RemoteMCPServer kagent's <c>POST /api/toolservers</c> takes (kagent.dev/v1alpha2), from a record.</summary>
internal static class KAgentServerObject
{
    public const string ManagedByLabel = "skanyxx.dev/managed-by";
    public const string ManagedBy = "skanyxx-toolservers";

    public static JsonObject For(ToolServerRecord record)
    {
        var spec = new JsonObject
        {
            ["description"] = record.Description,
            ["protocol"] = record.Protocol,
            ["url"] = record.Url
        };
        if (record.TimeoutSeconds is { } timeout)
            spec["timeout"] = $"{timeout}s";
        if (record.Headers.Count > 0)
            spec["headersFrom"] = new JsonArray([.. record.Headers.Select(h => (JsonNode)new JsonObject
            {
                ["name"] = h.Name,
                ["valueFrom"] = new JsonObject { ["type"] = "Secret", ["name"] = h.SecretName, ["key"] = h.SecretKey }
            })]);
        return new JsonObject
        {
            ["apiVersion"] = "kagent.dev/v1alpha2",
            ["kind"] = "RemoteMCPServer",
            ["metadata"] = new JsonObject
            {
                ["name"] = record.Name,
                ["namespace"] = record.Namespace,
                ["labels"] = new JsonObject { [ManagedByLabel] = ManagedBy }
            },
            ["spec"] = spec
        };
    }

    /// <summary>
    /// A fresh name for the Secret kagent creates with the server: an edit deletes and re-creates the server, and the old
    /// Secret may still be waiting for garbage collection (kagent refuses to reuse one owned by the old server).
    /// </summary>
    public static string CompanionSecret(string server) => $"{server}-headers-{Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()[..3])}";
}
