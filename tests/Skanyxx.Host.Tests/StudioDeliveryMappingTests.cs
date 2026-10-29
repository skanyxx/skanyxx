using System.Text.Json.Nodes;
using Skanyxx.Core.Models;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Studio;
using Skanyxx.Module.Agents.Studio.Ci;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Deploy;
using Skanyxx.Module.Agents.Studio.Features;
using Skanyxx.Module.Agents.Studio.Git;
using Skanyxx.Module.Agents.Studio.Reconcile;
using Skanyxx.Module.Agents.Studio.Templates;

namespace Skanyxx.Host.Tests;

/// <summary>
/// The studio's delivery path as pure mappings (D360–D367): templates render valid manifests, git's check statuses and
/// Argo's or kagent's deploy states become one vocabulary, and the timeline says where each change is.
/// </summary>
public sealed class StudioDeliveryMappingTests
{
    private static readonly StudioOptions Options = new() { McpServers = ["jira", "aws-cost"], CloudToolServers = ["aws-cost"] };

    // --- Templates (D360) ---

    [Fact]
    public void EveryTemplate_RendersAManifest_ThatReadsBackAndPassesTheFormRules()
    {
        Assert.Equal(["support", "research", "ops", "data", "cloud-ops"], StudioTemplates.All.Select(t => t.Id));
        foreach (var template in StudioTemplates.All)
        {
            var draft = template.Draft with { ModelConfig = "default-model-config" };
            Assert.Empty(DraftRules.Check(draft, Options));
            var (agent, grants) = AgentManifest.Render(draft, "kagent");
            Assert.Contains("kind: Agent", agent);
            Assert.Contains($"name: {draft.Name}", agent);
            Assert.Contains("company", grants);
            var (read, problem) = AgentManifest.Read(draft.Name, agent, grants, "kagent");
            Assert.Null(problem);
            Assert.Equal(draft.Instructions, read!.Instructions);
            Assert.Equal(draft.Grants, read.Grants);
            Assert.Empty(TextSecrets.Check(draft));
        }
        Assert.Equal(["cloud-ops"], StudioTemplates.All.Where(t => t.CloudTools).Select(t => t.Id));
    }

    // --- Cloud tools licence (D364) ---

    [Fact]
    public void CloudTools_AreRefusedWithoutTheLicence_AndAllowedWithIt()
    {
        var draft = new AgentDraft("costs", "", "default-model-config", "Answer.", [],
            [new McpToolChoice("aws-cost", ["get_costs"]), new McpToolChoice("jira", ["search"])], [], null);

        var refused = Assert.Single(CloudToolRules.Check(draft, Options, cloudToolsOn: false));
        Assert.Contains("'aws-cost'", refused);
        Assert.Contains("Skanyxx:Licence:CloudTools", refused);
        Assert.Contains("Settings", refused);
        Assert.Empty(CloudToolRules.Check(draft, Options, cloudToolsOn: true));
        Assert.Empty(CloudToolRules.Check(draft with { McpTools = [new McpToolChoice("jira", ["search"])] }, Options, cloudToolsOn: false));
        Assert.True(CloudToolRules.IsPaid("aws-cost", Options));
        Assert.False(CloudToolRules.IsPaid("jira", Options));
    }

    [Fact]
    public void ACredentialTypedIntoInstructions_IsRefusedInPlainWords()
    {
        var draft = new AgentDraft("x", "", "m", "Use the key gh" + "p_abcdefghijklmnopqrstuvwxyz0123456789AB to call git.", [], [], [], null);
        Assert.Contains("never go into an agent's text", Assert.Single(TextSecrets.Check(draft)));
    }

    // --- Check status mapping (D362) ---

    [Theory]
    [InlineData("success", CheckState.Passed)]
    [InlineData("warning", CheckState.Passed)]
    [InlineData("failure", CheckState.Failed)]
    [InlineData("error", CheckState.Failed)]
    [InlineData("pending", CheckState.Pending)]
    [InlineData("SUCCESS", CheckState.Passed)]
    [InlineData("something-new", CheckState.Pending)]
    public void OneRun_MapsFromGiteaOrGitHub(string status, CheckState expected)
    {
        Assert.Equal(expected, CheckStatusMap.State(status));
        // Gitea names it "status", GitHub "state": both read the same.
        Assert.Equal(expected, CheckStatusMap.From(Combined(new JsonObject { ["context"] = "ci", ["status"] = status })).State);
        Assert.Equal(expected, CheckStatusMap.From(Combined(new JsonObject { ["context"] = "ci", ["state"] = status })).State);
    }

    [Fact]
    public void Runs_Combine_FailureFirst_ThenPending_AndTheNewestPerContextCounts()
    {
        Assert.Equal(CheckState.None, CheckStatusMap.From(null).State);
        Assert.Equal(CheckState.None, CheckStatusMap.From(new JsonObject { ["statuses"] = new JsonArray() }).State);

        var checks = CheckStatusMap.From(Combined(
            new JsonObject { ["context"] = "validate", ["status"] = "success", ["target_url"] = "http://git/actions/1" },
            new JsonObject { ["context"] = "validate", ["status"] = "failure" },
            new JsonObject { ["context"] = "lint", ["status"] = "pending", ["target_url"] = "javascript:alert(1)" }));
        Assert.Equal(CheckState.Pending, checks.State);
        Assert.Equal(["validate", "lint"], checks.Runs.Select(r => r.Name));
        Assert.Equal("http://git/actions/1", checks.Runs[0].Url);
        Assert.Null(checks.Runs[1].Url); // only http(s) links are offered

        Assert.Equal(CheckState.Failed, CheckStatusMap.Combine([CheckState.Passed, CheckState.Pending, CheckState.Failed]));
        Assert.Equal(CheckState.Passed, CheckStatusMap.Combine([CheckState.Passed, CheckState.Passed]));
    }

    private static JsonObject Combined(params JsonObject[] statuses) => new() { ["statuses"] = new JsonArray([.. statuses]) };

    // --- Deploy status: Argo (D363) ---

    [Theory]
    [InlineData("Synced", "Healthy", "Succeeded", DeployState.Healthy)]
    [InlineData("OutOfSync", "Healthy", "Running", DeployState.Syncing)]
    [InlineData("Synced", "Progressing", "Succeeded", DeployState.Syncing)]
    [InlineData("Synced", "Degraded", "Succeeded", DeployState.Failed)]
    [InlineData("OutOfSync", "Missing", "Running", DeployState.Waiting)]
    [InlineData("OutOfSync", "Healthy", "Failed", DeployState.Failed)]
    [InlineData("Synced", "Suspended", "Succeeded", DeployState.Suspended)]
    public void Argo_ResourceStatus_Maps(string sync, string health, string phase, DeployState expected)
    {
        var status = ArgoStatusMap.For(FakeArgo.Application("helper", sync, health, phase), "helper", "kagent");
        Assert.Equal(expected, status.State);
        Assert.Equal("argo", status.Source);
    }

    [Fact]
    public void Argo_WithoutTheAgentsResource_IsWaiting_UnlessTheSyncFailed()
    {
        var app = FakeArgo.Application("other", "Synced", "Healthy");
        Assert.Equal(DeployState.Waiting, ArgoStatusMap.For(app, "helper", "kagent").State);
        Assert.Equal(DeployState.Failed, ArgoStatusMap.For(FakeArgo.Application("other", "Synced", "Healthy", "Error"), "helper", "kagent").State);
        Assert.Equal(DeployState.Unknown, ArgoStatusMap.For(new JsonObject(), "helper", "kagent").State);
        // Same name in another namespace is not this agent.
        Assert.Equal(DeployState.Waiting, ArgoStatusMap.For(app, "other", "elsewhere").State);
    }

    // --- Deploy status: the reconciler and kagent, when Argo is not configured (D363) ---

    [Fact]
    public void KAgent_Fallback_Maps()
    {
        static Agent Live(bool ready = true, bool accepted = true, bool suspended = false, bool merged = true) => new()
        {
            Name = "helper", Ready = ready, Accepted = accepted, Suspended = suspended,
            Labels = merged ? new() { [StudioNames.MergedLabel] = "true" } : []
        };

        Assert.Equal(DeployState.Healthy, KAgentStatusMap.For(Live(), null).State);
        Assert.Equal(DeployState.Syncing, KAgentStatusMap.For(Live(ready: false), null).State);
        Assert.Equal(DeployState.Syncing, KAgentStatusMap.For(Live(ready: false, accepted: false), null).State);
        Assert.Equal(DeployState.Healthy, KAgentStatusMap.For(Live(accepted: false), null).State);
        Assert.Equal(DeployState.Suspended, KAgentStatusMap.For(Live(suspended: true), null).State);
        Assert.Equal(DeployState.Waiting, KAgentStatusMap.For(Live(merged: false), null).State);
        Assert.Equal(DeployState.Waiting, KAgentStatusMap.For(null, new ReconcilePass(DateTimeOffset.UtcNow, 0, false)).State);
        var failed = KAgentStatusMap.For(null, new ReconcilePass(DateTimeOffset.UtcNow, 2, false));
        Assert.Equal(DeployState.Failed, failed.State);
        Assert.Contains("retries", failed.Detail);
        Assert.Equal("reconciler", failed.Source);
    }

    // --- The timeline (D365) ---

    [Fact]
    public void Timeline_OpenWithRunningChecks()
    {
        var steps = ChangeTimeline.Build(ChangeStatus.Open, new ChangeChecks(CheckState.Pending, [new CheckRun("ci", CheckState.Pending, "", null)]), null, false);
        Assert.Equal(["Draft", "Pull request opened", "Checks", "Merged", "Deployed"], steps.Select(s => s.Name));
        Assert.Equal([StepState.Done, StepState.Done, StepState.Active, StepState.Active, StepState.Waiting], steps.Select(s => s.State));
    }

    [Fact]
    public void Timeline_MergedAndHealthy_IsDoneThroughout()
    {
        var steps = ChangeTimeline.Build(ChangeStatus.Merged, new ChangeChecks(CheckState.Passed, [new CheckRun("ci", CheckState.Passed, "", null)]),
            new DeployStatus(DeployState.Healthy, "argo", "Argo CD: Synced, Healthy."), true);
        Assert.All(steps, s => Assert.Equal(StepState.Done, s.State));
        Assert.Equal("Argo CD: Synced, Healthy.", steps[^1].Detail);
    }

    [Fact]
    public void Timeline_FailedChecks_FailedDeploy_ClosedAndSuperseded()
    {
        var failed = ChangeTimeline.Build(ChangeStatus.Open, new ChangeChecks(CheckState.Failed, [new CheckRun("validate", CheckState.Failed, "", null)]), null, true);
        Assert.Equal(StepState.Failed, failed[2].State);
        Assert.Contains("validate", failed[2].Detail);
        Assert.Equal(StepState.Waiting, failed[3].State);

        var deployFailed = ChangeTimeline.Build(ChangeStatus.Merged, ChangeChecks.None, new DeployStatus(DeployState.Failed, "reconciler", "kagent did not accept the agent."), false);
        Assert.Equal(StepState.Skipped, deployFailed[2].State);
        Assert.Equal(StepState.Failed, deployFailed[4].State);

        var closed = ChangeTimeline.Build(ChangeStatus.Closed, ChangeChecks.None, null, false);
        Assert.Equal([StepState.Skipped, StepState.Skipped], closed.Skip(3).Select(s => s.State));

        var superseded = ChangeTimeline.Build(ChangeStatus.Merged, ChangeChecks.None, null, false, "Replaced by a later change (#9).");
        Assert.Equal(StepState.Done, superseded[4].State);
        Assert.Contains("#9", superseded[4].Detail);
    }

    // --- The CI template (D362) ---

    [Fact]
    public void TheCiTemplate_IsTheCommittedFile_AndKeepsItsTokenInASecret()
    {
        var committed = File.ReadAllText(Path.Combine(Repo.Root, "docs", "templates", "agents-repo", "validate-agents.yaml"));
        Assert.Equal(committed, CiTemplate.Text);
        Assert.Contains("api/studio/ci/validate", committed);
        Assert.Contains("${{ secrets.SKANYXX_CI_TOKEN }}", committed);
        Assert.Contains("pull_request", committed);
        Assert.DoesNotContain("set -x", committed);
    }
}
