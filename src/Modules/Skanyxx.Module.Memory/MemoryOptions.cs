using System.ComponentModel.DataAnnotations;
using Npgsql;

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
