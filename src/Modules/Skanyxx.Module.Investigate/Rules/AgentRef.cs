using System.Text.RegularExpressions;

namespace Skanyxx.Module.Investigate.Rules;

/// <summary>A kagent agent as <c>namespace/name</c>: both Kubernetes names (DNS labels).</summary>
public static partial class AgentRef
{
    public static bool IsValid(string? reference) => reference is not null && Pattern().IsMatch(reference);

    public static (string Namespace, string Name) Split(string reference) =>
        (reference[..reference.IndexOf('/')], reference[(reference.IndexOf('/') + 1)..]);

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?/[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\z")]
    private static partial Regex Pattern();
}
