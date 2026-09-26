namespace Skanyxx.Module.Sandboxes.Domain;

/// <summary>Kubernetes quantities, e.g. <c>500m</c> / <c>1Gi</c>.</summary>
public sealed record ResourceQuantity(string? Cpu, string? Memory);
