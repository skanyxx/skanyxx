using Skanyxx.Host.Setup.Guides;

namespace Skanyxx.Host.Setup;

/// <summary>Every guide, in the order the overview lists them. The one place a guide is registered.</summary>
public static class SetupGuides
{
    public static IReadOnlyList<SetupGuide> All { get; } =
    [
        ModelGuide.Guide, ChatGuide.Guide, MemoryGuide.Guide, LibraryGuide.Guide, StudioGuide.Guide, PluginsGuide.Guide,
        ToolServersGuide.Guide, SandboxesGuide.Guide, EmailGuide.Guide, EntraGuide.Guide, DragonflyGuide.Guide, QuotasGuide.Guide,
    ];

    public static SetupGuide? Find(string id) => All.FirstOrDefault(g => g.Id == id);
}
