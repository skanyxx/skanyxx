using System.Security.Cryptography;
using Ax.V1Alpha1;
using AxTask = Ax.V1Alpha1.Task;

namespace Skanyxx.Module.Sandboxes.Gateway;

/// <summary>
/// AX v0.3.1's <c>ObjectMeta</c> has no labels or annotations, so Skanyxx records who started a sandbox in the sandbox's
/// own env. Every <c>SKANYXX_</c> name is reserved: a caller cannot set one, so a sandbox cannot claim another owner
/// through Skanyxx. Anyone who can reach AX directly can (AX has no auth) — hence the NetworkPolicy requirement.
/// </summary>
internal static class SandboxEnv
{
    public const string ReservedPrefix = "SKANYXX_";
    public const string Owner = "SKANYXX_OWNER";

    /// <summary>
    /// The memory identity (grants are keyed by it). Fresh on every run that (re)activates the sandbox, so grants given to
    /// one run never pass to a later sandbox reusing the name. Informational for the image: the sandbox listener takes the
    /// agent from <see cref="MemoryToken"/>, never from a header (D076).
    /// </summary>
    public const string AgentId = "SKANYXX_AGENT_ID";

    /// <summary>The user the sandbox acts for (its owner). Informational: the listener reads the owner from the token.</summary>
    public const string UserId = "SKANYXX_USER_ID";

    /// <summary>
    /// D073/D076, set only when the sandbox binds a workspace with memory attached: the sandbox listener's
    /// <c>/mcp/memory</c> (the same URL as the workspace's <c>skanyxx-memory</c> MCP entry).
    /// </summary>
    public const string MemoryUrl = "SKANYXX_MEMORY_URL";

    /// <summary>
    /// D076: the signed per-sandbox token. The image sends it on every MCP request as <c>Authorization: Bearer
    /// $SKANYXX_MEMORY_TOKEN</c>; it is the sandbox's whole memory identity. Never an agent secret.
    /// </summary>
    public const string MemoryToken = "SKANYXX_MEMORY_TOKEN";

    public static bool IsReserved(string name) => name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>ax-&lt;name&gt;-&lt;8 hex&gt;</c>: at most 75 chars, inside the memory module's agent-id pattern.</summary>
    public static string NewAgentId(string sandboxName) => $"ax-{sandboxName}-{RandomNumberGenerator.GetHexString(8, lowercase: true)}";

    public static string? Read(AxTask sandbox, string name) =>
        sandbox.Spec?.Env.FirstOrDefault(e => e.Name == name)?.Value;

    public static IEnumerable<EnvVar> For(string owner, string agentId, (string Url, string Token)? memory)
    {
        yield return new EnvVar { Name = Owner, Value = owner };
        yield return new EnvVar { Name = AgentId, Value = agentId };
        yield return new EnvVar { Name = UserId, Value = owner };
        if (memory is var (url, token))
        {
            yield return new EnvVar { Name = MemoryUrl, Value = url };
            yield return new EnvVar { Name = MemoryToken, Value = token };
        }
    }
}
