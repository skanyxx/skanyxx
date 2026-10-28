namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>The setup guides of the Agents page, as fixed texts (no value from kagent or configuration is ever quoted).</summary>
internal static class SetupGuides
{
    public const string AdminNote = " Only an admin can fix this; send them these steps.";

    public static SetupGuide KAgentUnreachable(bool admin) => new("kagent-unreachable", "kagent cannot be reached",
        "Skanyxx asked kagent's controller for its agents and got no usable answer, so no agent can be listed, created or changed." + Note(admin),
        [
            "Check that kagent's controller runs in the cluster: `kubectl -n kagent get pods` lists kagent-controller as Running and Ready.",
            "Check Skanyxx's settings KAgent:BaseUrl and KAgent:Port (default 8083): they must point at the controller's Service as Skanyxx reaches it.",
            "Read the Skanyxx log for lines starting with \"kagent\": they say whether the call timed out, was refused or answered an error.",
            "Fix the cause, then reload the Agents page."
        ]);

    public static SetupGuide NoModels(string ns, bool admin) => new("no-models", "No model is configured",
        $"kagent has no ModelConfig in the namespace '{ns}', so an agent has nothing to run on." + Note(admin),
        [
            "Open the Model page in Skanyxx (owner) and save a model with its provider and API key.",
            $"Or create a ModelConfig in '{ns}' in kagent (kagent-ui → Models, or `kubectl apply`).",
            "Reload the Agents page: the model appears in the form's Model list."
        ]);

    public static SetupGuide NoMcpServers() => new("no-mcp-servers", "No MCP server is allow-listed",
        "Agents made here may only use MCP servers on the allow-list, and it is empty: the form offers no tools.",
        [
            "Add a RemoteMCPServer on the Tool Servers page (or in kagent).",
            "Add its name to the setting Studio:McpServers (one entry per server) and restart Skanyxx.",
            "Reload the Agents page: the server and the tools kagent discovered on it appear under Tools."
        ]);

    public static SetupGuide StudioNotConfigured(bool admin) => new("studio-not-configured", "Studio is not configured",
        "Your changes to agents are sent as Studio proposals (pull requests a supervisor reviews), and Studio has no agent repo yet." + Note(admin),
        [
            "Set Studio:Git:BaseUrl to the Gitea that holds the agent repo (the bundled one, e.g. http://gitea.skanyxx.svc:3000).",
            "Set Studio:Git:Token to the token of Skanyxx's git account (a secret: put it in the environment or a Kubernetes Secret, never in a file in git).",
            "Restart Skanyxx, then finish Studio's setup on the Studio page so the agent repo is created.",
            "Submit the proposal again."
        ]);

    private static string Note(bool admin) => admin ? "" : AdminNote;
}
