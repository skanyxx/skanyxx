namespace Skanyxx.Module.Agents.Studio;

/// <summary><c>Studio:Ci</c> (D362): the agent repo's CI asks the studio to validate agent folders.</summary>
public sealed class CiOptions
{
    /// <summary>
    /// The bearer token CI sends to <c>POST api/studio/ci/validate</c> (a repo secret there). Empty turns the endpoint
    /// off. Never logged, never returned.
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>Where the owner's "Add CI to the agent repo" commits the workflow: Gitea Actions' folder by default, <c>.github/workflows/…</c> for GitHub.</summary>
    public string WorkflowPath { get; set; } = ".gitea/workflows/validate-agents.yaml";
}
