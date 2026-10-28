using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Host.Tests.Analytics;

/// <summary>
/// A small org: Finance (billing: ann, sam the supervisor; payroll: bob) and Sales (eu: eve). Olga the owner and
/// nora are in no team.
/// </summary>
internal static class OrgFixture
{
    public static readonly AnalyticsViewer Owner = new("olga", IsOwner: true, IsSupervisor: true);
    public static readonly AnalyticsViewer Supervisor = new("sam", IsOwner: false, IsSupervisor: true);
    public static readonly AnalyticsViewer Employee = new("ann", IsOwner: false, IsSupervisor: false);

    public static readonly OrgSnapshot Snapshot = new(
        [Person("olga"), Person("sam"), Person("ann"), Person("bob"), Person("eve"), Person("nora")],
        [
            new TeamDto("billing", "Billing", "finance", ["ann", "sam"]),
            new TeamDto("payroll", "Payroll", "finance", ["bob"]),
            new TeamDto("eu", "Sales EU", "sales", ["eve"])
        ],
        [new DepartmentDto("finance", "Finance"), new DepartmentDto("sales", "Sales")]);

    private static PersonDto Person(string id) => new(id, $"{id}@example.com", null, ["employee"], false, false, true);
}
