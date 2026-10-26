namespace Skanyxx.Core.Platform.Plugins;

/// <summary>Where a plugin server in main stands.</summary>
public static class PluginServerStates
{
    /// <summary>kagent runs it as main and the stored header values describe it.</summary>
    public const string Deployed = "deployed";

    /// <summary>Created in kagent, which has not discovered its tools yet (kagent retries a server whose Secret it did not find, about a minute).</summary>
    public const string Starting = "starting";

    /// <summary>
    /// A header it declares has no value yet, or the values were entered for another host than its URL now names: not
    /// created, and its agents are held, until the owner enters them.
    /// </summary>
    public const string NeedsSecret = "needs-secret";

    /// <summary>Ready, and the reconciler creates or replaces it on its next pass.</summary>
    public const string Pending = "pending";

    /// <summary>kagent already has a server by this name that Skanyxx did not create: never touched.</summary>
    public const string Foreign = "foreign";

    /// <summary>Its file in main is not a valid plugin server: skipped.</summary>
    public const string Invalid = "invalid";
}
