using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Host.Data;

namespace Skanyxx.Host.Tests;

/// <summary>
/// Windows harness fix (todo §6): hosts running side by side each keep the leftover SQLite store in their own
/// content root, and stopping a host releases it, so its directory can be deleted. On Windows the delete used to
/// fail while a pooled SQLite connection still held skanyxx.db.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class ContentRootTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TwoHostsRunningSideBySide_HaveTheirOwnSqliteFile_AndReleaseIt()
    {
        // Started one after the other (Program's bootstrap logger and HostBuiltObserver are process-wide), then both run.
        var first = await HostApp.StartAsync(fixture.ConnectionString);
        HostApp? second = null;
        try
        {
            second = await HostApp.StartAsync(fixture.ConnectionString);
            var (firstFile, secondFile) = (SqliteFile(first), SqliteFile(second));
            var secondRoot = second.ContentRoot;

            Assert.Equal(Path.Combine(first.ContentRoot, "skanyxx.db"), firstFile);
            Assert.Equal(Path.Combine(secondRoot, "skanyxx.db"), secondFile);
            Assert.True(File.Exists(firstFile) && File.Exists(secondFile));

            // One host stops and its directory goes while the other (and the fixture's) keeps running on its own file.
            await second.DisposeAsync();
            second = null;
            Assert.False(Directory.Exists(secondRoot));
            Assert.True(File.Exists(firstFile));
        }
        finally
        {
            if (second is not null)
                await second.DisposeAsync();
            await first.DisposeAsync();
        }

        Assert.False(Directory.Exists(first.ContentRoot));
    }

    private static string SqliteFile(HostApp host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
    }
}
