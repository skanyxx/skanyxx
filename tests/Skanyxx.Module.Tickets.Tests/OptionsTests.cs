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

    // CR m4: ids are lowercase, so "Ana" would silently never match; it must stop startup instead.
    [Theory]
    [InlineData("Tickets:Supervisors:0", "Ana")]
    [InlineData("Tickets:Supervisors:0", "ana smith")]
    [InlineData("Tickets:MaxActiveRuns", "0")]
    [InlineData("Tickets:MaxPoolSize", "0")]
    public void InvalidSettings_FailValidation(string key, string value) =>
        Assert.Throws<OptionsValidationException>(() => Resolve(new() { [key] = value }));
}
