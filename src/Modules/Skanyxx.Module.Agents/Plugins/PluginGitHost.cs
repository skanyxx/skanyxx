namespace Skanyxx.Module.Agents.Plugins;

/// <summary>A git host besides github.com that plugin sources may be downloaded from (<see cref="PluginsOptions.GitHosts"/>).</summary>
public sealed class PluginGitHost
{
    /// <summary>The URL's authority: the host, plus <c>:port</c> when it has one.</summary>
    public string Host { get; set; } = "";

    /// <summary><c>gitea</c> or <c>gitlab</c>: how the host builds an archive URL for a commit.</summary>
    public string Kind { get; set; } = "";
}
