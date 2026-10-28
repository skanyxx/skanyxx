namespace Skanyxx.Module.Chat.Features.Agents;

/// <summary>A Kubernetes namespace or object name (an RFC 1123 label).</summary>
internal static class KubeName
{
    public const string Pattern = "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$";
}
