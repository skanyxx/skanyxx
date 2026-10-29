using System.Text.RegularExpressions;

namespace Skanyxx.Module.Investigate.Rules;

/// <summary>A step's id inside its workflow: what <c>{{steps.&lt;id&gt;}}</c> refers to.</summary>
public static partial class StepId
{
    public static bool IsValid(string? id) => id is not null && Pattern().IsMatch(id);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,31}\z")]
    private static partial Regex Pattern();
}
