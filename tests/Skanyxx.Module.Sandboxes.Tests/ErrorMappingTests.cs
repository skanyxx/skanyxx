using System.Net;
using System.Net.Http.Json;
using Grpc.Core;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class ErrorMappingTests : SandboxesTestBase
{
    public static TheoryData<StatusCode, HttpStatusCode, string> Mappings => new()
    {
        { StatusCode.InvalidArgument, HttpStatusCode.BadRequest, "AX rejected the request." },
        { StatusCode.AlreadyExists, HttpStatusCode.Conflict, "Already exists in AX." },
        { StatusCode.FailedPrecondition, HttpStatusCode.Conflict, "AX refused the change in the task's current state." },
        { StatusCode.Aborted, HttpStatusCode.Conflict, "AX refused the change in the task's current state." },
        { StatusCode.Unavailable, HttpStatusCode.BadGateway, "AX unreachable." },
        { StatusCode.DeadlineExceeded, HttpStatusCode.BadGateway, "AX unreachable." },
        { StatusCode.Internal, HttpStatusCode.BadGateway, "AX request failed." },
        { StatusCode.PermissionDenied, HttpStatusCode.BadGateway, "AX request failed." }
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task GrpcStatus_MapsToAFixedHttpCategory(StatusCode grpc, HttpStatusCode http, string message)
    {
        Ax.FailWith = grpc;

        foreach (var response in new[]
        {
            await App.Client().GetAsync("/api/sandboxes/tasks"),
            await App.Client().GetAsync("/api/sandboxes/tasks/fix-42"),
            await App.Client().GetAsync("/api/sandboxes/workspaces"),
            await App.Client().GetAsync("/api/sandboxes/models"),
            await RunAsync("fix-42")
        })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(http, response.StatusCode);
            Assert.Contains(message, body);
            Assert.DoesNotContain("hunter2", body);
            Assert.DoesNotContain("10.1.2.3", body);
        }
    }

    [Fact]
    public async Task NotFound_FromAnUpdate_Is404()
    {
        Ax.FailWith = StatusCode.NotFound;

        Assert.Equal(HttpStatusCode.NotFound, (await App.Client().GetAsync("/api/sandboxes/models")).StatusCode);
    }

    [Fact]
    public async Task ANetworkLevelFailure_Is502_WithoutTheAddress()
    {
        await using var app = await SandboxesApp.StartAsync("http://127.0.0.1:1");

        var response = await app.Client().GetAsync("/api/sandboxes/tasks");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("AX unreachable.", body);
        Assert.DoesNotContain("127.0.0.1", body);
    }

    [Fact]
    public async Task EveryCall_UsesTheConfiguredAtespace()
    {
        await RunAsync("fix-42");
        await App.Client().GetAsync("/api/sandboxes/tasks/fix-42");
        await App.Client().PostAsync("/api/sandboxes/tasks/fix-42/suspend", null);
        await App.Client().GetAsync("/api/sandboxes/workspaces");
        await App.Client().GetAsync("/api/sandboxes/models");
        await App.Client(SandboxesApp.Supervisor).PutAsJsonAsync("/api/sandboxes/workspaces/ws", new { });
        await App.Client().PostAsync("/api/sandboxes/tasks/fix-42/stop", null);

        Assert.NotEmpty(Ax.Atespaces);
        Assert.All(Ax.Atespaces, a => Assert.Equal(SandboxesApp.Atespace, a));
    }

    // CR M4 / N1: a client that goes away mid-call is not an AX failure — no warning, no 502 written.
    [Fact]
    public async Task AClientGoingAwayMidCall_IsNotLoggedAsAnAxFailure()
    {
        Ax.Seed("fix-42", "ana");
        Ax.HoldUpdates = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();

        var run = App.Client().PutAsJsonAsync("/api/sandboxes/tasks/fix-42", RunBody(), cancel.Token);
        await Ax.UpdateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        await App.Logs.WaitForRequestFinishedAsync("/api/sandboxes/tasks/fix-42");
        Assert.Empty(App.Logs.Warnings);
        Assert.DoesNotContain(App.Logs.Entries, e => e.Message.StartsWith("Request finished") && e.Message.Contains(" - 502"));
    }
}
