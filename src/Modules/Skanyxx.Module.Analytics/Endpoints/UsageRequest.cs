using System.Globalization;
using Skanyxx.Module.Analytics.Reports;
using Skanyxx.Module.Analytics.Scopes;

namespace Skanyxx.Module.Analytics.Endpoints;

/// <summary>
/// <c>?scope=me|user|team|department|org&amp;id=…&amp;range=7d|30d|90d|custom&amp;from=yyyy-MM-dd&amp;to=yyyy-MM-dd&amp;compare=true</c>.
/// </summary>
internal sealed class UsageRequest
{
    public string? Scope { get; set; }
    public string? Id { get; set; }
    public string? Range { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public bool Compare { get; set; }

    public (AnalyticsScope? Scope, DateRange? Range, string? Error) Parse(AnalyticsViewer viewer, DateOnly today)
    {
        if (!TryDate(From, out var from) || !TryDate(To, out var to))
            return (null, null, "Dates are yyyy-MM-dd.");
        var (range, error) = DateRange.Parse(Range, from, to, today);
        if (range is null)
            return (null, null, error);
        AnalyticsScope? scope = (Scope ?? "me") switch
        {
            "me" => AnalyticsScope.User(viewer.UserId),
            "org" => AnalyticsScope.Org,
            "user" when !string.IsNullOrWhiteSpace(Id) => new AnalyticsScope(ScopeKind.User, Id.Trim()),
            "team" when !string.IsNullOrWhiteSpace(Id) => new AnalyticsScope(ScopeKind.Team, Id.Trim()),
            "department" when !string.IsNullOrWhiteSpace(Id) => new AnalyticsScope(ScopeKind.Department, Id.Trim()),
            _ => null
        };
        return scope is null
            ? (null, null, "'scope' is me, org, or user/team/department with an 'id'.")
            : (scope, range, null);
    }

    private static bool TryDate(string? text, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrEmpty(text))
            return true;
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        date = parsed;
        return true;
    }
}
