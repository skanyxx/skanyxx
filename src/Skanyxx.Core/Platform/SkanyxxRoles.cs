namespace Skanyxx.Core.Platform;

/// <summary>The four hats (docs/design/roles.md). Role names are the claim values; one person may hold several.</summary>
public static class SkanyxxRoles
{
    public const string Owner = "owner";
    public const string Supervisor = "supervisor";
    public const string Builder = "builder";
    public const string Employee = "employee";

    /// <summary>What an invite or a role change may give. Never <see cref="Owner"/>: there is one, made at setup.</summary>
    public static readonly IReadOnlyList<string> Grantable = [Supervisor, Builder, Employee];
}
