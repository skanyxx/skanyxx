namespace Skanyxx.Module.Investigate.Domain;

/// <summary>
/// One node of a workflow tree. An agent step names a kagent agent (<c>namespace/name</c>) and a prompt template that
/// may use <c>{{input}}</c> and <c>{{steps.&lt;id&gt;}}</c> (the output of a step that has finished by then). A group runs
/// its <see cref="Steps"/> in <see cref="Mode"/>. Stored as JSON with its workflow, and copied into every run.
/// </summary>
public sealed class WorkflowStep
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public StepKind Kind { get; set; }

    public string? Agent { get; set; }

    public string? Prompt { get; set; }

    public GroupMode Mode { get; set; }

    public List<WorkflowStep> Steps { get; set; } = [];
}
