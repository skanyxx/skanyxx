namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>Where an agent comes from, which decides whether it may be changed directly (D252).</summary>
internal static class AgentSource
{
    /// <summary>Reconciled from the studio repo: git is its source of truth.</summary>
    public const string Studio = "studio";

    /// <summary>Installed by Skanyxx itself (the seed, the factory, ticket-flow stages, previews).</summary>
    public const string System = "system";

    /// <summary>Created on the Agents page.</summary>
    public const string Direct = "direct";

    /// <summary>Anything else in kagent (kagent-ui, kubectl, Helm).</summary>
    public const string External = "external";
}
