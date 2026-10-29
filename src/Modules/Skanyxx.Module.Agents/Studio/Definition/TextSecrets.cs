using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// A credential typed into a form ends up in git and in the agent's prompt (D361): the description and instructions
/// are scanned like a plugin's text (<see cref="SecretScan"/>).
/// </summary>
internal static class TextSecrets
{
    public static IReadOnlyList<string> Check(AgentDraft draft) =>
        SecretScan.Find(draft.Description + "\n" + draft.Instructions) is { } found
            ? [$"Instructions: they seem to hold {found}. Keys and passwords never go into an agent's text; a tool server keeps them."]
            : [];
}
