using System.Security.Cryptography;
using Ax.V1Alpha1;
using AxTask = Ax.V1Alpha1.Task;

namespace Skanyxx.Module.Sandboxes.Gateway;

/// <summary>
/// AX v0.3.1's <c>ObjectMeta</c> has no labels or annotations, so Skanyxx records who started a task in the task's
/// own env. Every <c>SKANYXX_</c> name is reserved: a caller cannot set one, so a task cannot claim another owner
/// through Skanyxx. Anyone who can reach AX directly can (AX has no auth) — hence the NetworkPolicy requirement.
/// </summary>
internal static class TaskEnv
{
    public const string ReservedPrefix = "SKANYXX_";
    public const string Owner = "SKANYXX_OWNER";

    /// <summary>
    /// The memory identity; the image sends it as <c>X-Agent-Id</c> to <c>/mcp/memory</c>. Fresh on every run that
    /// (re)activates the task, so grants given to one run never pass to a later task reusing the name.
    /// </summary>
    public const string AgentId = "SKANYXX_AGENT_ID";

    /// <summary>The user the task acts for; the image sends it as <c>X-User-Id</c> to <c>/mcp/memory</c>.</summary>
    public const string UserId = "SKANYXX_USER_ID";

    public static bool IsReserved(string name) => name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary><c>ax-&lt;name&gt;-&lt;8 hex&gt;</c>: at most 75 chars, inside the memory module's agent-id pattern.</summary>
    public static string NewAgentId(string taskName) => $"ax-{taskName}-{RandomNumberGenerator.GetHexString(8, lowercase: true)}";

    public static string? Read(AxTask task, string name) =>
        task.Spec?.Env.FirstOrDefault(e => e.Name == name)?.Value;

    public static IEnumerable<EnvVar> For(string owner, string agentId) =>
    [
        new EnvVar { Name = Owner, Value = owner },
        new EnvVar { Name = AgentId, Value = agentId },
        new EnvVar { Name = UserId, Value = owner }
    ];
}
