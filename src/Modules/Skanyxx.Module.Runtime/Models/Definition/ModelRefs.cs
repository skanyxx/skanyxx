using System.Text.RegularExpressions;

namespace Skanyxx.Module.Runtime.Models.Definition;

/// <summary>ModelConfig refs (<c>namespace/name</c>): both parts Kubernetes names (DNS-1123 labels).</summary>
internal static partial class ModelRefs
{
    public static bool IsKubeName(string? value) => value is { Length: > 0 and <= 63 } && KubeName().IsMatch(value);

    public static (string Namespace, string Name) Split(string configRef) =>
        configRef.IndexOf('/') is var slash and > 0 ? (configRef[..slash], configRef[(slash + 1)..]) : ("", configRef);

    [GeneratedRegex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$")]
    private static partial Regex KubeName();
}
