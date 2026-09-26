namespace Skanyxx.Host.Tests;

/// <summary>CR m1: a module that cannot register its services stops the Host instead of serving 500s.</summary>
[Collection(HostCollection.Name)]
public sealed class ModuleLoaderTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ModuleRegistrationFailure_StopsStartup()
    {
        // TicketsModule binds KAgent eagerly; an unparsable port throws inside its RegisterServices.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HostApp.StartAsync(fixture.ConnectionString, s => s["KAgent:Port"] = "not-a-port"));

        Assert.Equal("Module 'tickets' failed to register its services. Fix its configuration or set Modules:Enabled:tickets=false.", ex.Message);
    }
}
