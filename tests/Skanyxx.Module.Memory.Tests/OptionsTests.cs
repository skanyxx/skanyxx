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

    // D076/D077: a sandbox listener needs a signing key, and must not be the agents' mcp port.
    [Theory]
    [InlineData("9100", null, null)]
    [InlineData("9100", "too-short", null)]
    [InlineData("9100", "a-sandbox-token-key-of-at-least-32-characters", "9100")]
    [InlineData("0", "a-sandbox-token-key-of-at-least-32-characters", null)]
    public async Task ABadSandboxListener_StopsStartup(string port, string? key, string? mcpPort)
    {
        await using var app = MemoryApp.Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Memory"] = "Host=localhost",
            ["Memory:SandboxMcpPort"] = port,
            ["Memory:SandboxTokenKey"] = key,
            ["Memory:McpPort"] = mcpPort
        });

        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(TestContext.Current.CancellationToken));
    }
}
