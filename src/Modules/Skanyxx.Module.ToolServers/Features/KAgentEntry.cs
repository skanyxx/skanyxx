using Skanyxx.Module.ToolServers.Definition;

namespace Skanyxx.Module.ToolServers.Features;

/// <summary>One entry of kagent's <c>GET /api/toolservers</c>: <c>{ref, groupKind, discoveredTools: [{name, description}]}</c>.</summary>
internal sealed record KAgentEntry(string Ref, string Kind, IReadOnlyList<ToolView> Tools);
