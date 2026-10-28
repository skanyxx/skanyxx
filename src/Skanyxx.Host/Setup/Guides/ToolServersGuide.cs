namespace Skanyxx.Host.Setup.Guides;

/// <summary>kagent's MCP tool servers. Facts: README "API Endpoints" (ToolServers), "Install with Helm" (Network), "Studio".</summary>
public static class ToolServersGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "toolservers",
        Title: "Tool servers",
        Readme: "API Endpoints (ToolServers) and Install with Helm → Network",
        What: "The MCP tool servers kagent runs. Everyone sees the list; only the owner adds or deletes one. Studio agents may use a server only when it is in Studio:McpServers.",
        Prerequisites:
        [
            "kagent running and reachable from the Host (see the Chat guide).",
            "The MCP server's URL, reachable from kagent's controller and agent pods.",
        ],
        Settings:
        [
            new("KAgent:BaseUrl", "Skanyxx cannot reach kagent, so no tool server can be listed or added.",
                "localhost:8083 through the port-forward locally; the chart sets KAgent:IngressUrl.", Alternative: "KAgent:IngressUrl"),
            new("Studio:McpServers", "Builders cannot attach these servers to studio agents.",
                "The RemoteMCPServer names (in Studio:Namespace) builders may attach.", Optional: true),
        ],
        Local:
        [
            new("Keep kagent's port-forward up.", "scripts/dev/first-hour.sh status"),
            new("As the owner, open /ToolServers → Add Server and give the server's URL and protocol. Header values that are secrets belong in a Kubernetes Secret, not in the URL."),
            new("To let builders use it in Studio, add its name to Studio:McpServers and restart the Host.", "Studio__McpServers__0=<server name> scripts/dev/first-hour.sh host"),
        ],
        Helm:
        [
            new("Agent pods reach only port 443 outside the cluster ranges by default; a server inside the cluster or on another port needs a rule in networkPolicy.modelEgress.extra (and networkPolicy.controllerEgress.extra when controller egress is fenced)."),
            new("Add Studio__McpServers__0=<server name> through skanyxx.extraEnv, then upgrade.",
                "helm upgrade skanyxx deploy/helm/skanyxx -n <namespace> --reuse-values -f <your-values.yaml>"),
        ],
        Verify:
        [
            new("/ToolServers lists the server, and its tools appear once kagent has discovered them."),
        ],
        Errors:
        [
            new("The list does not load", "kagent is unreachable: check the port-forward (first-hour.sh status) or KAgent:IngressUrl."),
            new("The server never lists tools", "kagent cannot reach its URL: check networkPolicy.modelEgress / controllerEgress."),
        ]);
}
