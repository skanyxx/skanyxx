namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>An AX model configuration (used by AX's own components, not injected into tasks). The secret reference is not returned.</summary>
public sealed record SandboxModel(string Name, string Provider, string Model);
