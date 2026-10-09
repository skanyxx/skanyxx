using System.Reflection;
using System.Security.Claims;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Memory;
using Skanyxx.Module.Memory.Tests.Infrastructure;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>
/// CR M1: memory trusts a <see cref="LibraryUser"/>'s id and roles without checking them, so one may come only from a
/// signed-in person's principal: no public constructor, agents refused, and only the Host and memory build one.
/// </summary>
public sealed class LibraryUserTests
{
    [Fact]
    public void APerson_CarriesTheirIdAndRoles()
    {
        var user = People.Person("ana", SkanyxxRoles.Supervisor);
        var owner = People.Person("olivia", SkanyxxRoles.Owner);

        Assert.Equal(("ana", true, false), (user.UserId, user.IsSupervisor, user.IsOwner));
        Assert.Equal(("olivia", true, true), (owner.UserId, owner.IsSupervisor, owner.IsOwner));
    }

    // QA2 L2: FluentValidation skips child validators on null, so a null user must fail validation, not reach a handler.
    [Fact]
    public void ANullUser_FailsValidation_InEveryLibraryRequest()
    {
        Assert.False(new Features.Cards.RenameCardValidator().Validate(new RenameCardCommand(null!, "company", "a-key", "b-key", 1)).IsValid);
        Assert.False(new Features.Cards.LiftCardValidator().Validate(new LiftCardCommand(null!, "personal:ana", "a-key", "company")).IsValid);
        Assert.False(new Features.Library.OpenCardValidator().Validate(new OpenCardQuery(null!, "company", "a-key")).IsValid);
        Assert.False(new Features.Library.BrowseLibraryValidator().Validate(new BrowseLibraryQuery(null!, null, null)).IsValid);
    }

    [Fact]
    public void AnAnonymousPrincipal_HasNoUser_AndValidationRefusesIt() =>
        Assert.Null(LibraryUser.From(new ClaimsPrincipal(new ClaimsIdentity())).UserId);

    [Fact]
    public void AnAgentSecretPrincipal_IsRefused()
    {
        var agent = People.Principal("seed", AgentPrincipal.SchemeName);

        var ex = Assert.Throws<InvalidOperationException>(() => LibraryUser.From(agent));

        Assert.Equal("An agent is not a person: library requests need a signed-in user.", ex.Message);
        Assert.Equal(Access.AgentSecretAuthentication.SchemeName, AgentPrincipal.SchemeName); // one name, not two copies
    }

    /// <summary>Whatever scheme it came through, a principal carrying the agent's claim is an agent.</summary>
    [Fact]
    public void APrincipalWithTheAgentClaim_IsRefused()
    {
        var agent = People.Principal("seed", "Other");
        ((ClaimsIdentity)agent.Identity!).AddClaim(new Claim(AgentPrincipal.ActsForUsersClaim, "true"));

        Assert.Throws<InvalidOperationException>(() => LibraryUser.From(agent));
    }

    [Fact]
    public void ThereIsNoWayToMakeOneButFrom()
    {
        Assert.Empty(typeof(LibraryUser).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(typeof(LibraryUser).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Equal([nameof(LibraryUser.From)], typeof(LibraryUser).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(LibraryUser)).Select(m => m.Name));
    }

    /// <summary>
    /// Only the Host (its pages) and the memory module (its endpoints and handlers) touch a LibraryUser, so only they can
    /// build a library command: another module sending one would be acting as a person it is not. Checked over the
    /// modules the Host actually loads (its <c>modules/</c> folder, refreshed by building the solution).
    /// </summary>
    [Fact]
    public void OnlyTheHostAndMemory_UseLibraryUser()
    {
        var modules = Path.Combine(RepoRoot(), "src", "Skanyxx.Host", "modules");
        Assert.True(File.Exists(Path.Combine(modules, "Skanyxx.Module.Tickets.dll")), $"Build Skanyxx.sln first: no modules in {modules}.");
        var architecture = new ArchLoader()
            .LoadAssemblies(typeof(Program).Assembly, typeof(LibraryUser).Assembly)
            .LoadFilteredDirectory(modules, "Skanyxx.Module.*.dll")
            .Build();

        Types().That().DependOnAny(typeof(LibraryUser))
            .Should().ResideInNamespaceMatching(@"^Skanyxx\.Host(\..*)?$")
            .OrShould().ResideInNamespaceMatching(@"^Skanyxx\.Module\.Memory(\..*)?$")
            .OrShould().ResideInNamespace("Skanyxx.Core.Platform.Memory")
            .Check(architecture);
    }

    /// <summary>
    /// D110: memory trusts the studio contracts' grants and issues their secrets without a person's role check (the
    /// supervisor's merge was the check), so only the agents module (the studio reconciler) may send them.
    /// </summary>
    [Fact]
    public void OnlyTheStudio_SendsTheStudioMemoryContracts()
    {
        var modules = Path.Combine(RepoRoot(), "src", "Skanyxx.Host", "modules");
        Assert.True(File.Exists(Path.Combine(modules, "Skanyxx.Module.Agents.dll")), $"Build Skanyxx.sln first: no modules in {modules}.");
        var architecture = new ArchLoader()
            .LoadAssemblies(typeof(Program).Assembly, typeof(LibraryUser).Assembly)
            .LoadFilteredDirectory(modules, "Skanyxx.Module.*.dll")
            .Build();

        Types().That().DependOnAny(typeof(SetStudioGrantsCommand), typeof(IssueStudioSecretCommand), typeof(RemoveStudioAccessCommand),
                typeof(StudioPrincipalQuery), typeof(MarkStudioSecretDeployedCommand), typeof(SuspendStudioAgentCommand), typeof(StudioRepoQuery),
                typeof(RecordStudioRepoCommand))
            .Should().ResideInNamespaceMatching(@"^Skanyxx\.Module\.Agents\.Studio(\..*)?$")
            .OrShould().ResideInNamespaceMatching(@"^Skanyxx\.Module\.Memory(\..*)?$")
            .OrShould().ResideInNamespace("Skanyxx.Core.Platform.Memory")
            .Check(architecture);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "Skanyxx.sln")))
            dir = dir.Parent ?? throw new InvalidOperationException("Skanyxx.sln not found above " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
