namespace Skanyxx.Module.Runtime.Models.Features;

/// <param name="Namespace">The default model's namespace: where a new model goes unless the owner names another.</param>
internal sealed record ModelFormOptions(string Namespace, IReadOnlyList<ProviderOption> Providers);
