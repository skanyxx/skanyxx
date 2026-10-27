using Skanyxx.Core.Platform;

namespace Skanyxx.Module.ChatContext.Access;

/// <summary>Memory's scope grammar (memory-hierarchy.md) for who may read a session: <c>company</c> or <c>personal|team|department:&lt;id&gt;</c>.</summary>
internal static class SessionScope
{
    public const string Company = "company";

    public static string Personal(string userId) => $"personal:{userId}";

    public static bool IsValid(string? scope) =>
        scope == Company
        || scope?.Split(':', 2) is [("personal" or "team" or "department"), var id] && Identifier.IsValid(id);

    public static bool IsOrg(string scope) => scope.StartsWith("team:", StringComparison.Ordinal) || scope.StartsWith("department:", StringComparison.Ordinal);
}
