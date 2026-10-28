using Skanyxx.Module.ToolServers.Setup;

namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>Whether kagent answered, at which address (scheme, host and port), and the fix when it did not.</summary>
public sealed record KAgentStatus(bool Reachable, string Address, SetupFix? Fix);
