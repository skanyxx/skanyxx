using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using FluentValidation;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Skanyxx.Module.Memory.Tests;

public sealed class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Module = typeof(MemoryModule).Assembly;
    private static readonly Architecture Architecture = new ArchLoader().LoadAssemblies(Module).Build();

    private static readonly IObjectProvider<IType> DataLayer =
        Types().That().ResideInNamespace("Skanyxx.Module.Memory.Data").As("data layer");

    [Fact]
    public void Endpoints_DoNotTouchTheDatabase() =>
        Types().That().ResideInNamespace("Skanyxx.Module.Memory.Endpoints.Cards")
            .Or().ResideInNamespace("Skanyxx.Module.Memory.Endpoints.Grants")
            .Or().ResideInNamespace("Skanyxx.Module.Memory.Endpoints")
            .Should().NotDependOnAny(DataLayer)
            .Check(Architecture);

    [Fact]
    public void McpTools_DoNotTouchTheDatabase() =>
        Types().That().ResideInNamespace("Skanyxx.Module.Memory.Mcp")
            .Should().NotDependOnAny(DataLayer)
            .Check(Architecture);

    [Fact]
    public void Handlers_DoNotDependOnHttp() =>
        Classes().That().HaveNameEndingWith("Handler")
            .Should().NotDependOnAny(Types().That().ResideInNamespace("Microsoft.AspNetCore.Http"))
            .Check(Architecture);

    [Fact]
    public void Module_DoesNotReferenceHost() =>
        Assert.DoesNotContain(Module.GetReferencedAssemblies(), a => a.Name == "Skanyxx.Host");

    // BindingProblem is the global FastEndpoints error builder and prints a fixed message, so a FastEndpoints
    // validator would return meaningless 400s. Validation belongs in FluentValidation validators run by MediatR.
    [Fact]
    public void Module_HasNoFastEndpointsValidators() =>
        Assert.DoesNotContain(Module.GetTypes(), t => t.BaseType is { IsGenericType: true } b
            && b.GetGenericTypeDefinition() == typeof(FastEndpoints.Validator<>));

    [Fact]
    public void EveryRequest_HasExactlyOneValidator()
    {
        var requests = Module.GetTypes()
            .Where(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .ToList();
        var validated = Module.GetTypes()
            .Where(t => !t.IsAbstract)
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToList();

        Assert.NotEmpty(requests);
        foreach (var request in requests)
            Assert.True(validated.Count(v => v == request) == 1, $"{request.Name} must have exactly one validator.");
    }
}
