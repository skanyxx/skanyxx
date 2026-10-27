namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>A host's own content root, so nothing a host writes there is ever another host's.</summary>
public static class TempContentRoot
{
    public static string Create() => Directory.CreateTempSubdirectory("skanyxx-host-").FullName;

    public static void Delete(string path) => Directory.Delete(path, recursive: true);
}
