namespace Skanyxx.Module.Memory.Domain;

/// <summary>Ordered bottom-up: a lift may only go to a higher value (D049).</summary>
public enum ScopeLevel
{
    Personal,
    Team,
    Department,
    Company
}
