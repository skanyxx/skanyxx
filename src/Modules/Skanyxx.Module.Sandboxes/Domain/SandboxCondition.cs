namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>AX's condition without its message: that text comes from controller/Substrate errors and can carry internals.</summary>
public sealed record SandboxCondition(string Type, string Status, string Reason, DateTime? LastTransitionTime);
