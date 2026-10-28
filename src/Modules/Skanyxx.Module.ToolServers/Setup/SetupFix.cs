namespace Skanyxx.Module.ToolServers.Setup;

/// <summary>
/// One thing that is not working: a status line in plain words (<see cref="Title"/>, <see cref="Cause"/>), numbered steps
/// for a local run and for the Helm chart, how to verify, and a prompt for an AI coding agent. Names only, never a value.
/// </summary>
public sealed record SetupFix(
    string Code,
    string Title,
    string Cause,
    IReadOnlyList<FixStep> Local,
    IReadOnlyList<FixStep> Helm,
    IReadOnlyList<FixStep> Verify,
    string AgentPrompt);
