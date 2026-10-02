namespace Skanyxx.Host.Tests.Infrastructure;

public static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(System.IO.Path.Combine(dir.FullName, "Skanyxx.sln")))
            dir = dir.Parent ?? throw new InvalidOperationException("Skanyxx.sln not found above " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
