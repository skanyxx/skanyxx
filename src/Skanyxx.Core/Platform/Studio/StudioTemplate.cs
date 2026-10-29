namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// A starting point in the template gallery (D360): a role, what the agent does, and a filled form. The form is a draft:
/// the person renames it, picks the model and tools, and proposes (or applies) it like any other.
/// </summary>
/// <param name="CloudTools">It is meant to use cloud tool servers, a paid feature (D364).</param>
/// <param name="Locked">Cloud tools are not licensed: the template is shown, not usable.</param>
public sealed record StudioTemplate(string Id, string Title, string Role, string Summary, AgentDraft Draft, bool CloudTools, bool Locked);
