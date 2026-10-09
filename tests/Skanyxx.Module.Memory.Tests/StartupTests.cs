using Npgsql;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class StartupTests
{
    [Fact]
    public async Task UnreachableDatabase_StopsStartup()
    {
        await using var app = MemoryApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Memory"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2"
        });

        // Opening the migration lock's connection goes through EF's execution strategy, which wraps a transient failure.
        var error = await Assert.ThrowsAnyAsync<Exception>(() => app.StartAsync(TestContext.Current.CancellationToken));
        Assert.IsAssignableFrom<NpgsqlException>(error as NpgsqlException ?? error.InnerException);
    }
}
