using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Memory.Tests.Infrastructure;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>CR M3: memory needs identity's <see cref="IOrgMembership"/>; without it, startup fails instead of every card request.</summary>
public sealed class ModuleDependencyTests
{
    [Fact]
    public void Memory_DeclaresTheIdentityModule()
    {
        Assert.Equal(["identity"], new MemoryModule().Dependencies);
    }

    [Fact]
    public async Task Initialize_FailsWithoutOrgMembership_AndPassesWithIt()
    {
        await using var without = new ServiceCollection().BuildServiceProvider();
        await using var with = new ServiceCollection().AddSingleton<IOrgMembership, FakeOrgMembership>().BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new MemoryModule().InitializeAsync(without));
        await new MemoryModule().InitializeAsync(with);

        Assert.Contains("IOrgMembership", error.Message);
    }
}
