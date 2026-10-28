namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// The allow-list of fields a chip or a field statistic may name, each mapped to SQL written here, never from input:
/// the only thing a caller's text ever becomes is a parameter value. <c>role</c> is the actor's current role, read
/// from <c>identity_user_roles</c> (role ids are the role names).
/// </summary>
internal static class AuditFields
{
    public const string None = "(none)";
    public const string Role = "role";

    public static readonly IReadOnlyList<string> Names = ["action", "actor", "target", "module", "outcome", "ip", Role];

    /// <summary>The column a field reads; <see cref="Role"/> has none on the audit row.</summary>
    public static string Column(string field) => field switch
    {
        "action" => "a.\"Action\"",
        "actor" => "a.\"ActorId\"",
        "target" => "a.\"TargetId\"",
        "module" => "a.\"Module\"",
        "outcome" => "a.\"Outcome\"",
        "ip" => "a.\"RemoteIp\"",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not an audit field.")
    };
}
