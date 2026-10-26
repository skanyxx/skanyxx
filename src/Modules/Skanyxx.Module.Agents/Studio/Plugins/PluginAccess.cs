using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Plugins;

/// <summary>
/// Who may install from where (P11): third-party content fetched from git or uploaded as a zip is the owner's to bring in;
/// a plugin from the built-in catalog a builder's too. Either way it is only a proposal until it is merged: by a supervisor
/// for the catalog, by the owner for git and zip (D222).
/// </summary>
internal static class PluginAccess
{
    public const string NotInstaller = "Only the owner installs a plugin from git or a zip; builders install from the built-in catalog.";

    public const string NotInspector = "Only the owner reads a plugin from git or a zip; builders and supervisors browse the built-in catalog.";

    /// <summary>Reading a source: the catalog ships with Skanyxx; git and zip content is the owner's to fetch (P11).</summary>
    public static bool CanInspect(StudioUser user, string sourceKind) => sourceKind switch
    {
        "catalog" => user.CanEnter,
        "git" or "zip" => user.IsOwner,
        _ => false
    };

    /// <summary>Who merges an install from this source (D222), for the install's answer.</summary>
    public static string Merger(string sourceKind) => sourceKind is "git" or "zip" ? "The owner" : "A supervisor";

    public static bool CanInstall(StudioUser user, string sourceKind) => sourceKind switch
    {
        "catalog" => user.CanPropose,
        "git" or "zip" => user.IsOwner,
        _ => false
    };
}
