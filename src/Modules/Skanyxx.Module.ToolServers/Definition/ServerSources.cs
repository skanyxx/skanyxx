namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>Who keeps a server: what the page may change follows from it.</summary>
public static class ServerSources
{
    /// <summary>Created on this page: edited and deleted here.</summary>
    public const string Skanyxx = "skanyxx";

    /// <summary>The studio's memory or plugin server: the reconciler re-creates it, so it is changed in Studio/Plugins only.</summary>
    public const string Studio = "studio";

    /// <summary>Created elsewhere (kubectl, GitOps, kagent's UI): deleted here, not edited (its full spec is not known).</summary>
    public const string Other = "other";
}
