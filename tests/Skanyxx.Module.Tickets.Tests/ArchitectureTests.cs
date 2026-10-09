using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using FluentValidation;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Skanyxx.Module.Tickets.Tests;

public sealed class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Module = typeof(TicketsModule).Assembly;
    private static readonly Architecture Architecture = new ArchLoader().LoadAssemblies(Module).Build();

    [Fact]
    public void Endpoints_DoNotTouchTheDatabaseOrTheEngine() =>
        Types().That().ResideInNamespaceMatching(@"Skanyxx\.Module\.Tickets\.Endpoints.*")
            .Should().NotDependOnAny(Types().That().ResideInNamespace("Skanyxx.Module.Tickets.Data")
                .Or().ResideInNamespace("Skanyxx.Module.Tickets.Engine"))
            .Check(Architecture);

    [Fact]
    public void Handlers_DoNotDependOnHttp() =>
        Classes().That().HaveNameEndingWith("Handler")
            .Should().NotDependOnAny(Types().That().ResideInNamespace("Microsoft.AspNetCore.Http"))
            .Check(Architecture);

    [Fact]
    public void OnlyTheJiraSourceAndTheStageClient_MakeHttpCalls() =>
        Types().That().DependOnAny(typeof(HttpClient))
            .Should().Be(typeof(Sources.JiraTicketSource)).OrShould().Be(typeof(Engine.KAgentStageClient))
            .OrShould().Be(typeof(TicketsModule))
            .Check(Architecture);

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
