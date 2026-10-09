using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Identity;
using Skanyxx.Module.Sandboxes.Features.Offboarding;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

/// <summary>
/// D154: when identity announces that a person's account is disabled, their active sandbox tasks stop (AX delete);
/// nobody else's, none already ending, and nothing for a supervisor demotion alone. A task AX would not stop, or AX
/// itself unreachable, fails the publish after the rest were tried (identity answers 500; disabling again retries).
/// With sandboxes off, nothing is asked of AX.
/// </summary>
public sealed class OffboardingTests : SandboxesTestBase
{
    [Fact]
    public async Task ADisabledPersonsActiveTasks_Stop_AndNobodyElses()
    {
        Ax.Seed("ana-running", SandboxesApp.User, phase: "Running");
        Ax.Seed("ana-pending", SandboxesApp.User, phase: "Pending");
        Ax.Seed("ana-suspended", SandboxesApp.User, phase: "Suspended");
        Ax.Seed("ana-failed", SandboxesApp.User, phase: "Failed");
        Ax.Seed("dan-running", SandboxesApp.Other, phase: "Running");
        Ax.Seed("cli-made", owner: null, phase: "Running");

        await PublishAsync(new PrivilegesRevoked(SandboxesApp.User, "account disabled", AccountDisabled: true));

        Assert.Equal(3, Ax.DeleteCalls);
        Assert.All(new[] { "ana-running", "ana-pending", "ana-suspended" }, name => Assert.Equal("Terminating", Ax.Stored(name)!.Status.Phase));
        Assert.Equal("Failed", Ax.Stored("ana-failed")!.Status.Phase);
        Assert.Equal("Running", Ax.Stored("dan-running")!.Status.Phase);
        Assert.Equal("Running", Ax.Stored("cli-made")!.Status.Phase);
        Assert.Equal(3, App.Logs.Warnings.Count(w => w.Contains("stopped: account disabled")));
    }

    /// <summary>Losing supervisor is not a disable: the person's own tasks stay theirs (Ownership decides per request).</summary>
    [Fact]
    public async Task ASupervisorDemotion_StopsNothing()
    {
        Ax.Seed("ana-running", SandboxesApp.User, phase: "Running");

        await PublishAsync(new PrivilegesRevoked(SandboxesApp.User, "roles saved without supervisor"));

        Assert.Equal(0, Ax.DeleteCalls);
        Assert.Empty(Ax.Atespaces);
    }

    [Fact]
    public async Task ATaskThatWillNotStop_FailsThePublish_AfterTheOthersStopped()
    {
        Ax.Seed("ana-a", SandboxesApp.User, phase: "Running");
        Ax.Seed("ana-b", SandboxesApp.User, phase: "Running");
        Ax.Undeletable.Add("ana-a");

        var failure = await Assert.ThrowsAsync<RevocationFailedException>(() =>
            PublishAsync(new PrivilegesRevoked(SandboxesApp.User, "account disabled", AccountDisabled: true)));

        Assert.Contains("1 of its 2 sandbox tasks could not be stopped", failure.Message);
        Assert.DoesNotContain(FakeAx.SecretDetail, failure.Message);
        Assert.Equal("Running", Ax.Stored("ana-a")!.Status.Phase);
        Assert.Equal("Terminating", Ax.Stored("ana-b")!.Status.Phase);
        Assert.DoesNotContain(App.Logs.Warnings, w => w.Contains(FakeAx.SecretDetail));
    }

    [Fact]
    public async Task AxUnreachable_FailsThePublish()
    {
        Ax.Seed("ana-a", SandboxesApp.User, phase: "Running");
        Ax.FailWith = Grpc.Core.StatusCode.Unavailable;

        var failure = await Assert.ThrowsAsync<RevocationFailedException>(() =>
            PublishAsync(new PrivilegesRevoked(SandboxesApp.User, "account disabled", AccountDisabled: true)));

        Assert.Equal(StopTasksOnPrivilegesRevoked.Unreachable + ".", failure.Message); // the owner's words (D162)
        Assert.IsType<Grpc.Core.RpcException>(failure.InnerException);
        Assert.Equal(0, Ax.DeleteCalls);
    }

    /// <summary>CR L5: a task deleted by someone else between the read and the delete is stopped, not a failure.</summary>
    [Fact]
    public async Task ATaskGoneBeforeItsDelete_CountsAsStopped()
    {
        Ax.Seed("ana-a", SandboxesApp.User, phase: "Running");
        Ax.Seed("ana-b", SandboxesApp.User, phase: "Running");
        Ax.GoneOnDelete.Add("ana-a");

        await PublishAsync(new PrivilegesRevoked(SandboxesApp.User, "account disabled", AccountDisabled: true));

        Assert.Equal("Terminating", Ax.Stored("ana-b")!.Status.Phase);
        Assert.Equal(2, App.Logs.Warnings.Count(w => w.Contains("stopped: account disabled")));
    }

    /// <summary>D161: a person the Entra re-check refused (not disabled) loses their running tasks the same way.</summary>
    [Fact]
    public async Task AccessRemovedByEntra_StopsTheTasks_LikeADisable()
    {
        Ax.Seed("ana-running", SandboxesApp.User, phase: "Running");

        await PublishAsync(new PrivilegesRevoked(SandboxesApp.User, "Entra group re-check refused the account", AccessRemoved: true));

        Assert.Equal("Terminating", Ax.Stored("ana-running")!.Status.Phase);
    }

    /// <summary>D160: this handler calls AX, so it runs after the local revocations (memory's secrets).</summary>
    [Fact]
    public void TheStop_RunsAfterLocalRevocations()
    {
        Assert.Equal(NotificationOrderAttribute.External, AllHandlersPublisher.OrderOf(typeof(StopTasksOnPrivilegesRevoked)));
    }

    [Fact]
    public async Task WithSandboxesOff_NothingIsAskedOfAx()
    {
        Ax.Seed("ana-a", SandboxesApp.User, phase: "Running");
        await using var off = await StartAppAsync(s =>
        {
            s["Sandboxes:Enabled"] = null;
            s["Sandboxes:NetworkIsolationConfirmed"] = null;
        });

        await off.Services.GetRequiredService<IPublisher>()
            .Publish(new PrivilegesRevoked(SandboxesApp.User, "account disabled", AccountDisabled: true), TestContext.Current.CancellationToken);

        Assert.Empty(Ax.Atespaces);
        Assert.Equal("Running", Ax.Stored("ana-a")!.Status.Phase);
    }

    private Task PublishAsync(PrivilegesRevoked revoked) =>
        App.Services.GetRequiredService<IPublisher>().Publish(revoked, CancellationToken.None);
}
