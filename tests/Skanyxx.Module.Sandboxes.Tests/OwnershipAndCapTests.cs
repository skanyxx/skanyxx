using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class OwnershipAndCapTests : SandboxesTestBase
{
    [Theory]
    [InlineData("stop")]
    [InlineData("suspend")]
    [InlineData("resume")]
    public async Task AnotherUser_CannotManageTheTask(string action)
    {
        await RunAsync("fix-42");

        var response = await App.Client(SandboxesApp.Other).PostAsync($"/api/sandboxes/tasks/fix-42/{action}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, Ax.DeleteCalls);
        Assert.False(Ax.Stored("fix-42")!.Spec.Suspend);
        Assert.NotEqual("Terminating", Ax.Stored("fix-42")!.Status.Phase);
    }

    [Theory]
    [InlineData(SandboxesApp.User, "stop")]
    [InlineData(SandboxesApp.User, "suspend")]
    [InlineData(SandboxesApp.Supervisor, "stop")]
    [InlineData(SandboxesApp.Supervisor, "suspend")]
    [InlineData(SandboxesApp.Supervisor, "resume")]
    public async Task TheCreatorOrASupervisor_Can(string user, string action)
    {
        await RunAsync("fix-42");
        if (action == "resume")
            await App.Client().PostAsync("/api/sandboxes/tasks/fix-42/suspend", null);

        var response = await App.Client(user).PostAsync($"/api/sandboxes/tasks/fix-42/{action}", null);

        Assert.True(response.IsSuccessStatusCode, $"{user} {action}: {response.StatusCode}");
    }

    [Fact]
    public async Task AnotherUser_CannotOverwriteTheTask()
    {
        await RunAsync("fix-42");

        var response = await RunAsync("fix-42", SandboxesApp.Other);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(Ax.Updates);
        Assert.Equal("ana", Ax.Stored("fix-42")!.Spec.Env.Single(e => e.Name == "SKANYXX_OWNER").Value);
    }

    [Fact]
    public async Task ATaskMadeOutsideSkanyxx_IsSupervisorsOnly()
    {
        Ax.Seed("cli-made", owner: null);

        var user = await App.Client().PostAsync("/api/sandboxes/tasks/cli-made/stop", null);
        var supervisor = await App.Client(SandboxesApp.Supervisor).PostAsync("/api/sandboxes/tasks/cli-made/stop", null);

        Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, supervisor.StatusCode);
    }

    [Fact]
    public async Task Cap_CountsOnlyTheCallersActiveTasks()
    {
        Ax.Seed("old-failed", "ana", phase: "Failed");
        Ax.Seed("going", "ana", phase: "Terminating");
        Ax.Seed("dans", "dan", phase: "Running");
        Ax.Seed("parked", "ana", phase: "Suspended");

        var second = await RunAsync("t-2");
        var third = await RunAsync("t-3");
        var others = await RunAsync("t-dan", SandboxesApp.Other);

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, others.StatusCode);
        Assert.Null(Ax.Stored("t-3"));
    }

    [Fact]
    public async Task Cap_CountsAcrossPages()
    {
        for (var i = 0; i < 150; i++)
            Ax.Seed($"x-{i:D3}", "dan");
        Ax.Seed("zz-ana-1", "ana");
        Ax.Seed("zz-ana-2", "ana");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RunAsync("t-new")).StatusCode);
    }

    // SEC2 N5: AX drops unreadable records, so a short page is not the last one.
    [Fact]
    public async Task Cap_CountsPastAShortPage()
    {
        for (var i = 0; i < 150; i++)
            Ax.Seed($"x-{i:D3}", "dan");
        Ax.Unlistable.Add("x-000");
        Ax.Seed("zz-ana-1", "ana");
        Ax.Seed("zz-ana-2", "ana");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RunAsync("t-new")).StatusCode);
        Assert.Empty(Ax.Updates);
    }

    [Fact]
    public async Task Cap_DoesNotApplyToUpdatingYourOwnTask()
    {
        await RunAsync("t-1");
        await RunAsync("t-2");

        Assert.Equal(HttpStatusCode.OK, (await RunAsync("t-1")).StatusCode);
    }

    [Fact]
    public async Task ParallelRuns_CannotAllPassTheCap()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => RunAsync($"p-{i}")));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
    }

    // SEC M3: a replace runs new code under the task's memory identity, so nobody replaces someone else's task.
    [Fact]
    public async Task ASupervisor_CannotReplaceSomeoneElsesTask()
    {
        await RunAsync("fix-42");

        var response = await RunAsync("fix-42", SandboxesApp.Supervisor);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(Ax.Updates);
        Assert.Equal("ana", Ax.Stored("fix-42")!.Spec.Env.Single(e => e.Name == "SKANYXX_USER_ID").Value);
    }

    [Fact]
    public async Task AnOwnerlessTask_IsReplacedOnlyByASupervisor_WhoThenOwnsIt()
    {
        Ax.Seed("cli-made", owner: null);

        var user = await RunAsync("cli-made");
        var supervisor = await RunAsync("cli-made", SandboxesApp.Supervisor);

        Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
        Assert.Equal(HttpStatusCode.OK, supervisor.StatusCode);
        var env = Ax.Stored("cli-made")!.Spec.Env.ToDictionary(e => e.Name, e => e.Value);
        Assert.Equal(SandboxesApp.Supervisor, env["SKANYXX_OWNER"]);
        Assert.Equal(SandboxesApp.Supervisor, env["SKANYXX_USER_ID"]);
        Assert.Matches(@"^ax-cli-made-[0-9a-f]{8}\z", env["SKANYXX_AGENT_ID"]);
    }

    // SEC M2: re-running your own failed tasks used to skip the cap.
    [Theory]
    [InlineData("Failed")]
    [InlineData("Completed")]
    [InlineData("Terminating")]
    public async Task Cap_AppliesWhenARunReactivatesAnInactiveTask(string phase)
    {
        Ax.Seed("live-1", "ana");
        Ax.Seed("live-2", "ana");
        Ax.Seed("dead", "ana", phase);

        var response = await RunAsync("dead");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Empty(Ax.Updates);
        Assert.Equal(phase, Ax.Stored("dead")!.Status.Phase);
    }

    // SEC H1: the per-user cap trusts a spoofable header; the atespace ceiling does not.
    [Fact]
    public async Task GlobalCap_CountsEveryonesActiveTasks()
    {
        await using var app = await StartAppAsync(s => s["Sandboxes:MaxActiveTasks"] = "3");
        Ax.Seed("dans", "dan");
        Ax.Seed("eves", "eve", "Suspended");
        Ax.Seed("cli-made", owner: null, "Pending");
        Ax.Seed("old", "dan", "Failed");

        var response = await RunAsync("t-new", app: app);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Empty(Ax.Updates);
    }

    // CR M2: past the scan bound the count would be partial, so the run is refused, not waved through.
    [Fact]
    public async Task Cap_RefusesTheRun_WhenTheAtespaceIsTooBigToCount()
    {
        for (var i = 0; i <= 10_000; i++)
            Ax.Seed($"x-{i:D5}", "dan", "Failed");

        var response = await RunAsync("t-new");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(Ax.Updates);
        Assert.Contains(App.Logs.Warnings, w => w.Contains("too many to count"));
    }

    // CR2 B1 / SEC2 N2: the atespace ceiling holds however many users the callers claim to be.
    [Fact]
    public async Task ParallelRuns_ByDistinctUsers_CannotAllPassTheAtespaceCap()
    {
        await using var app = await StartAppAsync(s => s["Sandboxes:MaxActiveTasks"] = "3");
        Ax.HoldLists = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runs = Enumerable.Range(0, 12).Select(i => RunAsync($"burst-{i}", $"user-{i}", app: app)).ToList();
        Assert.True(await SpinUntil(() => Volatile.Read(ref Ax.ListCalls) >= 1, TimeSpan.FromSeconds(5)));
        await Task.Delay(300);
        Ax.HoldLists.SetResult();
        var responses = await Task.WhenAll(runs);

        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(3, Ax.Updates.Count);
    }

    // SEC2 N1: suspend/resume never brings back an inactive task — that goes through the counted PUT.
    [Theory]
    [InlineData("resume", "Failed")]
    [InlineData("resume", "Terminating")]
    [InlineData("resume", "Completed")]
    [InlineData("resume", "Running")]
    [InlineData("suspend", "Failed")]
    [InlineData("suspend", "Terminating")]
    [InlineData("suspend", "Suspended")]
    public async Task SuspendOrResume_OfATaskItWouldNotKeepActive_Is409(string action, string phase)
    {
        Ax.Seed("dead", "ana", phase);

        var response = await App.Client().PostAsync($"/api/sandboxes/tasks/dead/{action}", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(phase, Ax.Stored("dead")!.Status.Phase);
        Assert.False(Ax.Stored("dead")!.Spec.Suspend);
    }

    [Fact]
    public async Task ACountOverItsBudget_RefusesTheRun()
    {
        await using var app = await StartAppAsync(s =>
        {
            s["Sandboxes:CountBudgetSeconds"] = "1";
            s["Sandboxes:TimeoutSeconds"] = "30";
        });
        Ax.HoldLists = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var watch = Stopwatch.StartNew();
        var response = await RunAsync("t-new", app: app);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(8));
        Assert.Empty(Ax.Updates);
    }

    // CR M3: a run in flight must not overwrite a suspend that raced it.
    [Fact]
    public async Task ASuspend_RacingARun_IsNotUndone()
    {
        await RunAsync("fix-42");
        Ax.HoldUpdates = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = RunAsync("fix-42");
        await Ax.UpdateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var suspend = App.Client(SandboxesApp.Supervisor).PostAsync("/api/sandboxes/tasks/fix-42/suspend", null);
        await Task.WhenAny(suspend, Task.Delay(500));
        var suspendWaited = !suspend.IsCompleted;
        Ax.HoldUpdates.SetResult();

        Assert.Equal(HttpStatusCode.OK, (await run).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await suspend).StatusCode);
        Assert.True(suspendWaited, "suspend ran while the run was between its read and its write");
        Assert.True(Ax.Stored("fix-42")!.Spec.Suspend);
    }

    // SEC M4: a memory identity belongs to one run, never to a later task reusing the name.
    [Fact]
    public async Task AReusedName_GetsAFreshAgentId()
    {
        var first = await (await RunAsync("build")).Content.ReadFromJsonAsync<SandboxTask>(SandboxesApp.Json);
        await App.Client().PostAsync("/api/sandboxes/tasks/build/stop", null);
        Ax.Remove("build");

        var second = await (await RunAsync("build", SandboxesApp.Other)).Content.ReadFromJsonAsync<SandboxTask>(SandboxesApp.Json);

        Assert.Matches(@"^ax-build-[0-9a-f]{8}\z", second!.AgentId);
        Assert.NotEqual(first!.AgentId, second.AgentId);
    }

    [Fact]
    public async Task ReactivatingAFailedTask_GetsAFreshAgentId()
    {
        var first = await (await RunAsync("fix-42")).Content.ReadFromJsonAsync<SandboxTask>(SandboxesApp.Json);
        Ax.Stored("fix-42")!.Status.Phase = "Failed";

        var again = await (await RunAsync("fix-42")).Content.ReadFromJsonAsync<SandboxTask>(SandboxesApp.Json);

        Assert.NotEqual(first!.AgentId, again!.AgentId);
    }

    private static async Task<bool> SpinUntil(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                return false;
            await Task.Delay(20);
        }
        return true;
    }
}
