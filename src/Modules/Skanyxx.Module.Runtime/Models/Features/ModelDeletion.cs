namespace Skanyxx.Module.Runtime.Models.Features;

/// <param name="Agents">The agents (<c>namespace/name</c>) whose spec names the model.</param>
internal sealed record ModelDeletion(string Namespace, string Name, IReadOnlyList<string> Agents);
