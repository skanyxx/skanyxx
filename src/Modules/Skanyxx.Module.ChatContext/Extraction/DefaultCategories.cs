namespace Skanyxx.Module.ChatContext.Extraction;

/// <summary>The built-in categories (D239): word stems a company's work talk usually falls into. Configurable.</summary>
public static class DefaultCategories
{
    public static readonly IReadOnlyDictionary<string, string[]> All = new Dictionary<string, string[]>
    {
        ["billing"] = ["refund", "invoice", "payment", "billing", "price", "pricing", "charge", "subscription", "discount"],
        ["deployment"] = ["deploy", "release", "rollout", "rollback", "helm", "pipeline", "kubernetes", "cluster", "container"],
        ["incident"] = ["incident", "outage", "error", "failure", "failing", "crash", "exception", "downtime", "bug"],
        ["access"] = ["access", "permission", "role", "login", "password", "rbac", "grant", "account", "sign"],
        ["data"] = ["database", "postgres", "query", "migration", "schema", "backup", "table", "index"],
        ["security"] = ["security", "vulnerab", "secret", "token", "encrypt", "audit", "credential", "cve"],
        ["people"] = ["vacation", "holiday", "salary", "hiring", "onboard", "offboard", "payroll", "leave"],
        ["customer"] = ["customer", "support", "complaint", "client", "contract", "order"],
        ["documentation"] = ["documentation", "docs", "guide", "readme", "howto", "manual", "procedure"]
    };
}
