using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features;

/// <summary>Resource bounds (<c>Sandboxes:MaxCpu</c>/<c>MaxMemory</c>) and the defaults filled in for anything left out.</summary>
internal static class ResourcePolicy
{
    /// <summary>Null when the requested resources are acceptable.</summary>
    public static string? Problem(SandboxResources? resources, SandboxesOptions options)
    {
        if (resources is null)
            return null;
        var (requests, limits) = (resources.Requests, resources.Limits);
        string?[] cpu = [requests?.Cpu, limits?.Cpu];
        string?[] memory = [requests?.Memory, limits?.Memory];

        if (cpu.Concat(memory).Any(q => q is not null && !KubeQuantity.TryParse(q, out _)))
            return "Resources are Kubernetes quantities, e.g. cpu '500m', memory '1Gi'.";
        if (cpu.Any(q => KubeQuantity.Exceeds(q, options.MaxCpu)) || memory.Any(q => KubeQuantity.Exceeds(q, options.MaxMemory)))
            return $"Resources may not exceed cpu {options.MaxCpu} and memory {options.MaxMemory}.";
        if (KubeQuantity.Exceeds(requests?.Cpu, limits?.Cpu) || KubeQuantity.Exceeds(requests?.Memory, limits?.Memory))
            return "A request may not exceed its limit.";
        return null;
    }

    /// <summary>Every request and limit set: a missing request is the default (never above the limit), a missing limit the default (never below the request).</summary>
    public static SandboxResources Apply(SandboxResources? resources, SandboxesOptions options)
    {
        var (requests, limits) = (resources?.Requests, resources?.Limits);
        var cpu = requests?.Cpu ?? KubeQuantity.Min(options.DefaultCpuRequest, limits?.Cpu);
        var memory = requests?.Memory ?? KubeQuantity.Min(options.DefaultMemoryRequest, limits?.Memory);
        return new SandboxResources(
            new ResourceQuantity(cpu, memory),
            new ResourceQuantity(
                limits?.Cpu ?? KubeQuantity.Max(options.DefaultCpuLimit, cpu),
                limits?.Memory ?? KubeQuantity.Max(options.DefaultMemoryLimit, memory)));
    }
}
