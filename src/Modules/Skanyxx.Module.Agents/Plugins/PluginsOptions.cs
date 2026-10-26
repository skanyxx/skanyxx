namespace Skanyxx.Module.Agents.Plugins;

/// <summary>
/// <c>Plugins</c> configuration: where git plugin sources may be downloaded from, and how much, and which plugin MCP
/// server URLs are accepted. The one place these switches are read.
/// </summary>
public sealed class PluginsOptions
{
    public const string Section = "Plugins";

    /// <summary>Git hosts besides github.com, e.g. <c>{ "Host": "git.acme.com", "Kind": "gitea" }</c>; any other host is refused.</summary>
    public List<PluginGitHost> GitHosts { get; set; } = [];

    /// <summary>
    /// Lets a git download reach loopback, private, link-local, unique-local and CGNAT addresses. Off by default: a
    /// plugin URL must not become a way into the cluster's own network (SSRF). On only for a local Gitea.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>
    /// Lets a plugin's MCP server URL name an internal address (a non-public IP, localhost, a single-label host, or a
    /// <c>.svc</c>, <c>.cluster.local</c>, <c>.local</c>, <c>.internal</c> name). Off by default: kagent connects to that
    /// URL from inside the cluster, with the owner's header values.
    /// </summary>
    public bool AllowPrivateServers { get; set; }

    /// <summary>Accepts <c>http://</c> git hosts and MCP server URLs. Off by default; on only for local development.</summary>
    public bool AllowHttp { get; set; }

    public long MaxDownloadBytes { get; set; } = 20 * 1024 * 1024;
}
