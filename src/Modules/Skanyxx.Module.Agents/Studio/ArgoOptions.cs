namespace Skanyxx.Module.Agents.Studio;

/// <summary>
/// <c>Studio:Argo</c> (D363): when set, the studio reads the deploy status of merged agents from this Argo CD
/// Application; when not, from the reconciler and kagent.
/// </summary>
public sealed class ArgoOptions
{
    /// <summary>Argo CD's API server as the Host reaches it, e.g. <c>https://argocd-server.argocd.svc</c>.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>A read-only Argo CD account token (<c>applications, get</c>). Never logged, never returned.</summary>
    public string Token { get; set; } = "";

    /// <summary>The Application that compares the agent repo with kagent (D546: it reports, the reconciler applies).</summary>
    public string Application { get; set; } = "skanyxx-agents";

    public int TimeoutSeconds { get; set; } = 10;

    public bool Configured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Token);
}
