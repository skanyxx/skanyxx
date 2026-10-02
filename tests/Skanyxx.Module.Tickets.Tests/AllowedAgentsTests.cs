using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Tickets.Engine;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class AllowedAgentsTests
{
    private static string[] Resolve(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Key, (string?)s.Value))
                .Append(KeyValuePair.Create("ConnectionStrings:Tickets", (string?)"Host=unused")))
            .Build();
        var services = new ServiceCollection();
        new TicketsModule().RegisterServices(services, configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<TicketsOptions>>().Value.AllowedAgents;
    }

    [Fact]
    public void Unset_MeansTheFiveTicketAgents() =>
        Assert.Equal(DefaultPipeline.Agents, Resolve());

    [Fact]
    public void Configured_ReplacesTheDefault_RatherThanAddingToIt() =>
        Assert.Equal(["team-a/triage"], Resolve(("Tickets:AllowedAgents:0", "team-a/triage")));
}
