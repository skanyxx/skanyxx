using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Features.Studio;

/// <summary>Rules for one grant from git: a shared scope (never personal: a studio agent has no user), at most 50.</summary>
internal static class StudioGrantRules
{
    public const int MaxGrants = 50;

    public static bool IsSharedScope(string? scope) =>
        Scope.TryParse(scope, out var parsed) && parsed.Level != ScopeLevel.Personal;
}
