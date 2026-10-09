using Microsoft.Data.Sqlite;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// A host's own content root, so its leftover SQLite store (<c>skanyxx.db</c>, AppDbContext) is never another host's.
/// Microsoft.Data.Sqlite pools connections, and a pooled connection keeps the file open after the host has stopped:
/// Windows then refuses to delete it ("being used by another process"), so the pools are cleared first.
/// </summary>
public static class TempContentRoot
{
    public static string Create() => Directory.CreateTempSubdirectory("skanyxx-host-").FullName;

    public static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(path, recursive: true);
    }
}
