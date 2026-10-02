using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class OptionsTests
{
    private static TicketsOptions Resolve(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Tickets"] = "Host=localhost";
        settings["Tickets:Source"] = "local";
        settings["Tickets:LocalPath"] = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "tickets.json");
        var services = new ServiceCollection();
        new TicketsModule().RegisterServices(services, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider().GetRequiredService<IOptions<TicketsOptions>>().Value;
    }

    [Fact]
    public void Defaults_CapRunsPerUserAndInTotal_AndBoundThePool()
    {
        var options = Resolve([]);

        Assert.Equal(5, options.MaxActiveRunsPerUser);
        Assert.Equal(20, options.MaxActiveRuns);
        Assert.Equal(40, options.MaxPoolSize);
    }

    // Supervisors come from role claims now; a Tickets:Supervisors list left in an old config must not stop startup.
    [Fact]
    public void LeftoverSupervisorsKey_IsIgnored() =>
        Assert.Equal(20, Resolve(new() { ["Tickets:Supervisors:0"] = "Not A Valid Id" }).MaxActiveRuns);

    [Theory]
    [InlineData("Tickets:MaxActiveRuns", "0")]
    [InlineData("Tickets:MaxPoolSize", "0")]
    public void InvalidSettings_FailValidation(string key, string value) =>
        Assert.Throws<OptionsValidationException>(() => Resolve(new() { [key] = value }));
}
