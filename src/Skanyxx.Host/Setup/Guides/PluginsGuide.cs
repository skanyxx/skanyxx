namespace Skanyxx.Host.Setup.Guides;

/// <summary>Claude Code plugins installed through the studio's pull requests. Facts: README "Plugins".</summary>
public static class PluginsGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "plugins",
        Title: "Plugins",
        Readme: "Plugins",
        What: "Installs a Claude Code plugin's remote MCP servers and agents into kagent through one studio pull request. Sources: the built-in catalog, a git repository pinned to a commit, or a zip. Nothing is live until the pull request is merged on /Studio.",
        Prerequisites:
        [
            "The studio set up (see the Studio guide): plugins are pull requests in skanyxx-agents.",
            "For a plugin from a git host other than github.com: that host listed in Plugins:GitHosts.",
            "Least-privilege tokens for any header a plugin server needs: every chat user of its agents acts with the owner's values.",
        ],
        Settings:
        [
            new("Studio:Git:BaseUrl", "There is no agent repo, so no plugin can be installed.", "See the Studio guide."),
            new("Studio:Git:Token", "Skanyxx cannot open the install pull request.", "See the Studio guide."),
            new("Plugins:GitHosts", "Only github.com and the built-in catalog can be used as plugin sources.",
                "A list of {Host, Kind} with Kind gitea or gitlab, e.g. Plugins__GitHosts__0__Host=git.acme.com, Plugins__GitHosts__0__Kind=gitea.", Optional: true),
        ],
        Local:
        [
            new("Set up the studio first.", "scripts/dev/first-hour.sh git"),
            new("For a plugin repository in the local Gitea (development only, never a shared install):",
                "Plugins__GitHosts__0__Host=127.0.0.1:3300 Plugins__GitHosts__0__Kind=gitea Plugins__AllowHttp=true Plugins__AllowPrivateNetworks=true scripts/dev/first-hour.sh host"),
            new("Open /Plugins, inspect a catalog entry (microsoft-learn, deepwiki) and install it; merge the pull request on /Studio."),
        ],
        Helm:
        [
            new("Add your git hosts through skanyxx.extraEnv (Plugins__GitHosts__0__Host, Plugins__GitHosts__0__Kind). Keep Plugins:AllowHttp and Plugins:AllowPrivateServers off."),
            new("Fence kagent's controller if plugins come from anyone but you: networkPolicy.controllerEgress.enabled=true with controllerEgress.kubeApiCidrs (kubectl get endpoints kubernetes -n default)."),
            new("Set a Data Protection certificate (skanyxx.dataProtection.certificateSecret) so stored header values are not protected by a key ring in clear."),
        ],
        Verify:
        [
            new("Install a catalog plugin, merge it on /Studio: /Plugins shows its servers deployed and its agents appear in Chat."),
        ],
        Errors:
        [
            new("Server stays \"needs secret\"", "The owner enters every declared header value on /Plugins."),
            new("Server stays \"starting\" for about a minute", "Normal on kagent 0.10: it retries once the server's Secret exists."),
            new("Source refused (400) for a git host", "List the host in Plugins:GitHosts with its kind; pin a full 40-character commit."),
            new("409 digest on install", "The source changed since it was inspected: inspect it again."),
        ]);
}
