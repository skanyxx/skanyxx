using System.ComponentModel.DataAnnotations;
using Npgsql;
using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Memory;

public sealed class MemoryOptions
{
    public const string Section = "Memory";

    /// <summary>Bound from <c>ConnectionStrings:Memory</c>.</summary>
    [Required]
    public string ConnectionString { get; set; } = "";

    /// <summary>D037: default top-k published cards per search.</summary>
    [Range(1, 20)]
    public int SearchTopK { get; set; } = 5;

    /// <summary>Writes (upsert, lift) per minute for one caller id.</summary>
    [Range(1, 10_000)]
    public int UpsertsPerMinute { get; set; } = 30;

    /// <summary>Writes per minute from everyone together, however many agents and users are writing.</summary>
    [Range(1, 100_000)]
    public int UpsertsPerMinuteTotal { get; set; } = 300;

    /// <summary>Connections in memory's own Npgsql pool (distinct from tickets' by its Application Name).</summary>
    [Range(1, 1_000)]
    public int MaxPoolSize { get; set; } = 40;

    /// <summary>
    /// A second Kestrel port that serves <c>/mcp/memory</c> and nothing else (the Helm chart: 8081, never behind the
    /// ingress): MCP answers 404 on every other port, every other path 404 on this one. Unset: <c>/mcp/memory</c> is on
    /// every port the Host listens on (local dev).
    /// </summary>
    [Range(1, 65_535)]
    public int? McpPort { get; set; }

    /// <summary>
    /// D077: a Kestrel port that serves <c>/mcp/memory</c> to sandboxes and nothing else. A request there is identified
    /// only by its sandbox token (D076): agent secrets, <c>X-Agent-Id</c> and <c>X-User-Id</c> mean nothing on it.
    /// Unset: no sandbox listener, and no sandbox token is accepted anywhere.
    /// </summary>
    [Range(1, 65_535)]
    public int? SandboxMcpPort { get; set; }

    /// <summary>D076: the HMAC-SHA256 key sandbox tokens are signed with (UTF-8, at least 32 characters). Changing it voids every token.</summary>
    public string SandboxTokenKey { get; set; } = "";

    /// <summary>How long a sandbox token lives; a sandbox running longer gets a new one when its owner re-runs it.</summary>
    [Range(1, 720)]
    public int SandboxTokenHours { get; set; } = 24;

    internal const int MinTokenKeyChars = SandboxMemory.MinTokenKeyChars;

    /// <summary>Sandbox tokens can be issued and checked: a listener and a key.</summary>
    internal bool SandboxTokensOn => SandboxMcpPort is not null && SandboxTokenKey is { Length: >= MinTokenKeyChars };

    /// <summary>The ports <c>/mcp/memory</c> is restricted to, or empty when it is on every port (no McpPort).</summary>
    internal IReadOnlyList<int> McpOnlyPorts => McpPort is int mcp ? SandboxMcpPort is int sandbox ? [mcp, sandbox] : [mcp] : [];

    /// <summary>Where bodies larger than the card go (D180); off unless an endpoint is set.</summary>
    public ObjectStoreOptions ObjectStore { get; set; } = new();

    /// <summary>
    /// The configured string with this module's pool settings. Npgsql keys pools by connection string, so the
    /// module-suffixed Application Name keeps memory and tickets from sharing (and exhausting) one pool, even when both
    /// connection strings are identical. A configured name (e.g. per replica) is kept as the prefix.
    /// </summary>
    internal NpgsqlConnectionStringBuilder ConnectionSettings()
    {
        var settings = new NpgsqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = MaxPoolSize };
        settings.ApplicationName = $"{(string.IsNullOrEmpty(settings.ApplicationName) ? "skanyxx" : settings.ApplicationName)}-memory";
        return settings;
    }
}
