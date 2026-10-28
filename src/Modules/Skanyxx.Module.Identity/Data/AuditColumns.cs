namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// The generated columns of <c>identity_audit</c>. An action is <c>&lt;area&gt;.&lt;verb&gt;</c>; a verb ending in
/// <c>refused</c> is a refusal, one ending in <c>failed</c> a failure, any other a success.
/// </summary>
internal static class AuditColumns
{
    public const string ModuleSql = """split_part("Action", '.', 1)""";

    public const string OutcomeSql = """CASE WHEN "Action" LIKE '%refused' THEN 'refused' WHEN "Action" LIKE '%failed' THEN 'failed' ELSE 'success' END""";

    public const string Success = "success";
    public const string Refused = "refused";
    public const string Failed = "failed";
}
