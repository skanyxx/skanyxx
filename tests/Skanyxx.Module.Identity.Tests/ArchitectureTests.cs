using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using FluentValidation;
using MediatR;
using Skanyxx.Core.Platform.Identity;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Skanyxx.Module.Identity.Tests;

public sealed class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Module = typeof(IdentityModule).Assembly;
    private static readonly Architecture Architecture = new ArchLoader().LoadAssemblies(Module).Build();

    [Fact]
    public void Endpoints_DoNotTouchTheDatabase() =>
        Types().That().ResideInNamespace("Skanyxx.Module.Identity.Endpoints")
            .Should().NotDependOnAny(Types().That().ResideInNamespace("Skanyxx.Module.Identity.Data"))
            .Check(Architecture);

    [Fact]
    public void Handlers_DoNotDependOnHttp() =>
        Classes().That().HaveNameEndingWith("Handler")
            .Should().NotDependOnAny(Types().That().ResideInNamespace("Microsoft.AspNetCore.Http"))
            .Check(Architecture);

    [Fact]
    public void Module_DoesNotReferenceHost() =>
        Assert.DoesNotContain(Module.GetReferencedAssemblies(), a => a.Name == "Skanyxx.Host");

    [Fact]
    public void Module_HasNoFastEndpointsValidators() =>
        Assert.DoesNotContain(Module.GetTypes(), t => t.BaseType is { IsGenericType: true } b
            && b.GetGenericTypeDefinition() == typeof(FastEndpoints.Validator<>));

    /// <summary>The module's own requests and the identity contracts it handles for the Host (Core.Platform.Identity).</summary>
    [Fact]
    public void EveryRequest_HasExactlyOneValidator()
    {
        var requests = Module.GetTypes().Concat(typeof(SignInCommand).Assembly.GetTypes().Where(t => t.Namespace == typeof(SignInCommand).Namespace))
            .Where(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .ToList();
        var validated = Module.GetTypes()
            .Where(t => !t.IsAbstract)
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToList();

        Assert.Equal(30, requests.Count);
        foreach (var request in requests)
            Assert.True(validated.Count(v => v == request) == 1, $"{request.Name} must have exactly one validator.");
    }
}
