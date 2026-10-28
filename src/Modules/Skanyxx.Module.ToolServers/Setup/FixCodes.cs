namespace Skanyxx.Module.ToolServers.Setup;

/// <summary>Every not-working state the Tool servers page explains.</summary>
public static class FixCodes
{
    /// <summary>kagent's address is on this machine and nothing answers: the port-forward is not running.</summary>
    public const string KAgentPortForward = "kagent-port-forward";

    /// <summary>kagent's address is elsewhere and nothing answers (refused, DNS, timeout).</summary>
    public const string KAgentUnreachable = "kagent-unreachable";

    /// <summary>kagent answered, with an error.</summary>
    public const string KAgentRefused = "kagent-refused";

    /// <summary>Skanyxx created the server, kagent does not list it.</summary>
    public const string NotRegistered = "not-registered";

    /// <summary>A server Skanyxx did not create lists no tools.</summary>
    public const string DiscoveryFailed = "discovery-failed";

    /// <summary>A server Skanyxx created lists no tools: kagent cannot reach its URL.</summary>
    public const string ServerUnreachable = "server-unreachable";

    /// <summary>No tools, and a header comes from a Secret Skanyxx did not create.</summary>
    public const string HeaderSecret = "header-secret";

    /// <summary>No tools from an https server: the certificate, or the URL path and protocol.</summary>
    public const string Tls = "tls";

    /// <summary>A stdio MCPServer: listed, not managed or run from Skanyxx.</summary>
    public const string StdioUnsupported = "stdio-unsupported";
}
