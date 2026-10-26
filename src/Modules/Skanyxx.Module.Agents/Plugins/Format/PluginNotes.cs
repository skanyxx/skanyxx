using Skanyxx.Core.Platform.Plugins;

namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>The three kinds of report line (<see cref="PluginNote.Status"/>).</summary>
internal static class PluginNotes
{
    public const string ChangedStatus = "changed";
    public const string UnsupportedStatus = "unsupported";
    public const string RefusedStatus = "refused";

    public static PluginNote Changed(string item, string reason) => new(item, ChangedStatus, reason);

    public static PluginNote Unsupported(string item, string reason) => new(item, UnsupportedStatus, reason);

    public static PluginNote Refused(string item, string reason) => new(item, RefusedStatus, reason);
}
