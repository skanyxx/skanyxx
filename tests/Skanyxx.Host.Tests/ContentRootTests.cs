namespace Skanyxx.Host.Tests;

/// <summary>
/// Windows harness fix (todo §6, D174): hosts running side by side each have their own content root, and stopping a
/// host releases it, so its directory can be deleted.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class ContentRootTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TwoHostsRunningSideBySide_HaveTheirOwnContentRoot_AndReleaseIt()
    {
        // Started one after the other (Program's bootstrap logger and HostBuiltObserver are process-wide), then both run.
        var first = await HostApp.StartAsync(fixture.ConnectionString);
        HostApp? second = null;
        try
        {
            second = await HostApp.StartAsync(fixture.ConnectionString);
            var secondRoot = second.ContentRoot;

            Assert.NotEqual(first.ContentRoot, secondRoot);
            Assert.True(Directory.Exists(first.ContentRoot) && Directory.Exists(secondRoot));

            // One host stops and its directory goes while the other (and the fixture's) keeps running.
            await second.DisposeAsync();
            second = null;
            Assert.False(Directory.Exists(secondRoot));
            Assert.True(Directory.Exists(first.ContentRoot));
        }
        finally
        {
            if (second is not null)
                await second.DisposeAsync();
            await first.DisposeAsync();
        }

        Assert.False(Directory.Exists(first.ContentRoot));
    }
}
