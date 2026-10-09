using Microsoft.Extensions.DependencyInjection;
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

        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
    }

    // Supervisors come from role claims now; a Memory:Supervisors list left in an old config must not stop startup.
    [Fact]
    public async Task LeftoverSupervisorsKey_IsIgnored()
    {
        await using var app = MemoryApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Memory"] = "Host=localhost",
            ["Memory:Supervisors:0"] = "Not A Valid Id"
        });

        var options = app.Services.GetRequiredService<IOptions<MemoryOptions>>().Value;

        Assert.Equal(5, options.SearchTopK);
    }
}
