using Microsoft.Extensions.Options;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

public sealed class OptionsTests
{
    [Theory]
    [InlineData(null, "5")]
    [InlineData("Host=localhost", "0")]
    public async Task InvalidMemoryConfig_StopsStartup(string? connectionString, string topK)
    {
        await using var app = MemoryApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Memory"] = connectionString,
            ["Memory:SearchTopK"] = topK
        });

        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync());
    }

    // CR m4: ids are lowercase, so "Ana" would silently never match; it must stop startup instead.
    [Theory]
    [InlineData("Ana")]
    [InlineData("ana smith")]
    public async Task AnInvalidSupervisorId_StopsStartup(string supervisor)
    {
        await using var app = MemoryApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Memory"] = "Host=localhost",
            ["Memory:Supervisors:0"] = supervisor
        });

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync());
        Assert.Contains("Memory:Supervisors", error.Message);
    }
}
