using System.Net;
using System.Net.Http.Json;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

/// <summary>Owner and supervisor rights come from the signed-in principal; the old X-User-Id header changes nothing.</summary>
public sealed class AuthenticationTests : SandboxesTestBase
{
    [Fact]
    public async Task TheOwner_IsTheSignedInUser_NotTheHeader()
    {
        var ana = App.Client();
        ana.DefaultRequestHeaders.Add("X-User-Id", SandboxesApp.Other);

        var response = await ana.PutAsJsonAsync("/api/sandboxes/tasks/fix-42", RunBody(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var env = Ax.Stored("fix-42")!.Spec.Env.ToDictionary(e => e.Name, e => e.Value);
        Assert.Equal(SandboxesApp.User, env["SKANYXX_OWNER"]);
        Assert.Equal(SandboxesApp.User, env["SKANYXX_USER_ID"]);
    }

    [Fact]
    public async Task AnotherUser_ClaimingTheOwnerInTheHeader_CannotStop()
    {
        await RunAsync("fix-42");
        var dan = App.Client(SandboxesApp.Other);
        dan.DefaultRequestHeaders.Add("X-User-Id", SandboxesApp.User);

        var response = await dan.PostAsync("/api/sandboxes/tasks/fix-42/stop", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(SkanyxxRoles.Owner, true)]
    [InlineData(SkanyxxRoles.Supervisor, true)]
    [InlineData(SkanyxxRoles.Employee, false)]
    [InlineData(SkanyxxRoles.Builder, false)]
    public async Task Role_DecidesSupervisorRights(string role, bool supervises)
    {
        await RunAsync("fix-42");
        var olga = App.ClientAs("olga", role);

        var workspace = await olga.PutAsJsonAsync("/api/sandboxes/workspaces/ws", new { }, cancellationToken: TestContext.Current.CancellationToken);
        var suspend = await olga.PostAsync("/api/sandboxes/tasks/fix-42/suspend", null, TestContext.Current.CancellationToken);

        Assert.Equal(supervises, workspace.IsSuccessStatusCode);
        Assert.Equal(supervises, suspend.IsSuccessStatusCode);
        if (!supervises)
        {
            Assert.Equal(HttpStatusCode.Forbidden, workspace.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, suspend.StatusCode);
        }
    }

    [Fact]
    public async Task SupervisorByName_WithoutRole_IsNotASupervisor()
    {
        await RunAsync("fix-42");

        var response = await App.ClientAs(SandboxesApp.Supervisor).PostAsync("/api/sandboxes/tasks/fix-42/stop", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
