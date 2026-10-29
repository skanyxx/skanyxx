namespace Skanyxx.Module.Quotas.Data;

public static class QuotaScopes
{
    public const string User = "user";
    public const string Team = "team";
    public const string Department = "department";

    public static readonly IReadOnlyList<string> All = [User, Team, Department];
}
