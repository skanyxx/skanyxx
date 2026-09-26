using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class OptionsTests
{
    private static IServiceCollection Register(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        new SandboxesModule().RegisterServices(services, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }

    private static SandboxesOptions Resolve(Dictionary<string, string?> settings)
    {
        settings.TryAdd("Sandboxes:Enabled", "true");
        return Register(settings).BuildServiceProvider().GetRequiredService<IOptions<SandboxesOptions>>().Value;
    }

    [Fact]
    public void Defaults_AreValid()
    {
        var options = Resolve([]);

        Assert.Equal("http://ax-server.ax-system.svc:8080", options.Address);
        Assert.Equal("default", options.Atespace);
        Assert.Empty(options.AllowedImages);
        Assert.Equal(120, options.WatchSeconds);
        Assert.False(options.NetworkIsolationConfirmed);
    }

    // SEC H2: off unless explicitly enabled.
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void NotEnabled_RegistersNothing(string? enabled) =>
        Assert.Empty(Register(new() { ["Sandboxes:Enabled"] = enabled, ["Sandboxes:AllowedImages:0"] = "ghcr.io/acme/" }));

    [Fact]
    public void AConfiguredAllowlist_ReplacesTheDefault() =>
        Assert.Equal(["ghcr.io/acme/"], Resolve(new()
        {
            ["Sandboxes:AllowedImages:0"] = "ghcr.io/acme/",
            ["Sandboxes:NetworkIsolationConfirmed"] = "true"
        }).AllowedImages);

    // SEC H2: no image may run until the operator confirms the network is fenced.
    [Fact]
    public void AllowedImages_WithoutConfirmedNetworkIsolation_StopsStartup()
    {
        var error = Assert.Throws<OptionsValidationException>(() => Resolve(new() { ["Sandboxes:AllowedImages:0"] = "ghcr.io/acme/" }));

        Assert.Contains("NetworkIsolationConfirmed", error.Message);
    }

    // CR M2: memory attach stays unreachable until D076/D077.
    [Theory]
    [InlineData("http://skanyxx.skanyxx.svc:8080/mcp/memory")]
    [InlineData("skanyxx/mcp/memory")]
    public void AnyMemoryMcpUrl_StopsStartup(string url)
    {
        var error = Assert.Throws<OptionsValidationException>(() => Resolve(new() { ["Sandboxes:MemoryMcpUrl"] = url }));

        Assert.Contains("D076/D077", error.Message);
    }

    [Theory]
    [InlineData("Sandboxes:Address", "not a url")]
    [InlineData("Sandboxes:Address", "ftp://ax:8080")]
    [InlineData("Sandboxes:Atespace", "")]
    [InlineData("Sandboxes:Atespace", "Not_A_Label")]
    [InlineData("Sandboxes:MaxActiveTasksPerUser", "0")]
    [InlineData("Sandboxes:MaxActiveTasks", "0")]
    [InlineData("Sandboxes:AllowedImages:0", "")]
    [InlineData("Sandboxes:Supervisors:0", "Ana")]
    [InlineData("Sandboxes:MaxCpu", "lots")]
    [InlineData("Sandboxes:MaxCpu", "500m")]
    [InlineData("Sandboxes:MaxMemory", "512Mi")]
    [InlineData("Sandboxes:DefaultCpuRequest", "2")]
    [InlineData("Sandboxes:DefaultMemoryLimit", "16Gi")]
    [InlineData("Sandboxes:MaxWatchesPerUser", "0")]
    [InlineData("Sandboxes:KeepAliveSeconds", "0")]
    public void InvalidSettings_FailValidation(string key, string value) =>
        Assert.Throws<OptionsValidationException>(() => Resolve(new() { [key] = value, ["Sandboxes:NetworkIsolationConfirmed"] = "true" }));
}
