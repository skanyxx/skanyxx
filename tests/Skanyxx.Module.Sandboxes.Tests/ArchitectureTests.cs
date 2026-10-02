using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using FluentValidation;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Module = typeof(SandboxesModule).Assembly;
    private static readonly Architecture Architecture = new ArchLoader().LoadAssemblies(Module).Build();

    [Fact]
    public void Endpoints_DoNotTouchAxOrGrpc() =>
        Types().That().ResideInNamespaceMatching(@"Skanyxx\.Module\.Sandboxes\.Endpoints.*")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"Ax\.V1Alpha1.*")
                .Or().ResideInNamespace("Skanyxx.Module.Sandboxes.Gateway")
                .Or().ResideInNamespaceMatching(@"Grpc\..*"))
            .Check(Architecture);

    [Fact]
    public void Domain_DoesNotDependOnAxWireTypes() =>
        Types().That().ResideInNamespace("Skanyxx.Module.Sandboxes.Domain")
            .Should().NotDependOnAny(Types().That().ResideInNamespaceMatching(@"Ax\.V1Alpha1.*"))
            .Check(Architecture);

    [Fact]
    public void Handlers_DoNotDependOnHttp() =>
        Classes().That().HaveNameEndingWith("Handler")
            .Should().NotDependOnAny(Types().That().ResideInNamespace("Microsoft.AspNetCore.Http"))
            .Check(Architecture);

    [Fact]
    public void OnlyTheGateway_CallsTheAxClient() =>
        Types().That().DependOnAny(typeof(global::Ax.V1Alpha1.AX.AXClient))
            .Should().Be(typeof(Gateway.AxGateway)).OrShould().Be(typeof(SandboxesModule))
            .OrShould().ResideInNamespaceMatching(@"Ax\.V1Alpha1.*")
            .Check(Architecture);

    [Fact]
    public void Module_DoesNotReferenceOtherModules() =>
        Assert.DoesNotContain(Module.GetReferencedAssemblies(), a => a.Name!.StartsWith("Skanyxx.Module.") || a.Name == "Skanyxx.Host");

    [Fact]
    public void NoDomainTypeIsCalledTask() =>
        Assert.DoesNotContain(Module.GetTypes(), t => t.Namespace?.StartsWith("Skanyxx.") == true
            && t.Name is "Task" or "KAgentTask" or "Run");

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
