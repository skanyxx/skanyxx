namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>Binds a workspace into a task; the first one is the command's working directory. <c>Goal</c> is handed to AX's environment-setup agent, not to the task.</summary>
public sealed record WorkspaceMount(string Name, string? Path = null, string? Goal = null);
