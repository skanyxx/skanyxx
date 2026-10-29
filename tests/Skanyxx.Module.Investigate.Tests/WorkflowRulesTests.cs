using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;
using Skanyxx.Module.Investigate.Tests.Infrastructure;
using static Skanyxx.Module.Investigate.Tests.Infrastructure.InvestigateApp;

namespace Skanyxx.Module.Investigate.Tests;

/// <summary>Tree validation: depth and size caps, ids, step fields, and which earlier answers a prompt may read.</summary>
public sealed class WorkflowRulesTests
{
    private const string A = "kagent/researcher";

    private static IReadOnlyList<string> Validate(params WorkflowStep[] steps) => WorkflowRules.Validate(steps, maxDepth: 4, maxSteps: 10, maxPromptChars: 200);

    [Fact]
    public void AValidTree_HasNoErrors()
    {
        var errors = Validate(
            Agent("s1", A, "Look into {{input}}"),
            Group("g1", GroupMode.Parallel,
                Agent("s2", A, "Logs for {{steps.s1}}"),
                Group("g2", GroupMode.Sequential, Agent("s3", A, "{{input}}"), Agent("s4", A, "Check {{steps.s3}} and {{ steps.s1 }}"))),
            Agent("s5", A, "Sum up {{steps.s2}}, {{steps.s4}} and {{steps.g1}}"));

        Assert.Empty(errors);
    }

    [Fact]
    public void AnEmptyWorkflow_IsRefused() => Assert.Equal(["Add at least one step."], Validate());

    [Fact]
    public void DeeperThanTheCap_IsRefused()
    {
        var tooDeep = Group("g1", GroupMode.Sequential, Group("g2", GroupMode.Sequential, Group("g3", GroupMode.Sequential,
            Group("g4", GroupMode.Sequential, Agent("s1", A, "x")))));

        Assert.Contains("Steps nest at most 4 levels deep.", Validate(tooDeep));
        Assert.Empty(Validate(Group("g1", GroupMode.Sequential, Group("g2", GroupMode.Sequential, Group("g3", GroupMode.Sequential, Agent("s1", A, "x"))))));
    }

    [Fact]
    public void MoreStepsThanTheCap_IsRefused_GroupsCounting()
    {
        var steps = Enumerable.Range(1, 9).Select(i => Agent($"s{i}", A, "x")).ToArray();

        Assert.Empty(Validate(steps));
        Assert.Equal(["A workflow has at most 10 steps (groups count); this one has 11."],
            Validate([.. steps, Group("g1", GroupMode.Parallel, Agent("s10", A, "x"))]));
    }

    [Fact]
    public void DuplicateIds_BadIds_AndMissingFields_AreRefused()
    {
        var errors = Validate(
            Agent("s1", A, "x"), Agent("s1", A, "y"),
            Agent("Bad Id", A, "x", title: "Bad"),
            new WorkflowStep { Id = "s3", Title = "No agent", Kind = StepKind.Agent, Prompt = "x" },
            new WorkflowStep { Id = "s4", Title = "No prompt", Kind = StepKind.Agent, Agent = A, Prompt = " " },
            new WorkflowStep { Id = "s5", Title = "", Kind = StepKind.Agent, Agent = A, Prompt = "x" },
            Agent("s6", A, new string('x', 201), title: "Long"),
            Group("g1", GroupMode.Parallel),
            new WorkflowStep { Id = "s7", Title = "Agent with children", Kind = StepKind.Agent, Agent = A, Prompt = "x", Steps = [Agent("s8", A, "x")] });

        Assert.Contains("Two steps share the id 's1'.", errors);
        Assert.Contains("Step 'Bad': its id must be 1-32 lowercase letters, digits or '-'.", errors);
        Assert.Contains("Step 'No agent': pick an agent.", errors);
        Assert.Contains("Step 'No prompt': write what the agent should do.", errors);
        Assert.Contains("Step 's5': give it a title of at most 120 characters.", errors);
        Assert.Contains("Step 'Long': the prompt is longer than 200 characters.", errors);
        Assert.Contains("Step 'g1': a group needs at least one step.", errors);
        Assert.Contains("Step 'Agent with children': an agent step cannot hold steps; make it a group.", errors);
    }

    [Fact]
    public void APrompt_CannotReadALaterStep_AParallelSibling_ItselfOrANonexistentOne()
    {
        var errors = Validate(
            Agent("s1", A, "{{steps.s2}}", title: "First"),
            Group("g1", GroupMode.Parallel, Agent("s2", A, "{{steps.s3}}", title: "Left"), Agent("s3", A, "{{input}}", title: "Right")),
            Agent("s4", A, "{{steps.s4}} {{steps.nope}}", title: "Last"));

        Assert.Equal(
        [
            "Step 'First' uses {{steps.s2}}, but that step has not finished by then.",
            "Step 'Left' uses {{steps.s3}}, but that step has not finished by then.",
            "Step 'Last' uses {{steps.s4}}, but that step has not finished by then.",
            "Step 'Last' uses {{steps.nope}}, but that step does not exist."
        ], errors);
    }

    [Fact]
    public void ANullStepOrList_IsRefused_NotThrown()
    {
        var step = Agent("s1", A, "x");
        step.Steps = null!;

        Assert.Single(WorkflowRules.Validate([step], 4, 10, 200));
        Assert.Single(WorkflowRules.Validate([null!], 4, 10, 200));
    }

    [Fact]
    public void Render_FillsInputAndEarlierAnswers_AndLeavesOtherBracesAlone()
    {
        var text = PromptTemplate.Render("Q: {{input}} / A: {{ steps.s1 }} / B: {{steps.s9}} / {{other}}", "why?", new Dictionary<string, string> { ["s1"] = "because" });

        Assert.Equal($"Q: {Prompts.Input("why?")} / A: {Prompts.Step("s1", "because")} / B: {Prompts.Step("s9", "")} / {{{{other}}}}", text);
        Assert.Equal(["s1", "s9"], PromptTemplate.References("{{steps.s1}} {{steps.s9}} {{steps.s1}}"));
    }
}
