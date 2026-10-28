namespace Skanyxx.Module.Runtime.Models.Definition;

/// <param name="Block">The spec's provider block (<c>openAI</c>, <c>ollama</c>, …).</param>
/// <param name="Fields">The block's fields the form edits: strings, numbers and switches. Nested settings, lists and
/// inline credentials (<c>azureAdToken</c>) are not among them: an edit keeps them as they are.</param>
/// <param name="Endpoints">Fields naming where requests (and so the stored key) go.</param>
/// <param name="Required">Fields Skanyxx requires on top of kagent's <c>requiredParams</c>.</param>
/// <param name="TakesKey">False for Ollama: no API key at all.</param>
internal sealed record ProviderSchema(
    string Block, IReadOnlyList<ParamField> Fields, IReadOnlyList<string> Endpoints, IReadOnlyList<string> Required, bool TakesKey = true);
