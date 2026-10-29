namespace Skanyxx.Module.Quotas.Features;

/// <summary>
/// A person's limit this month: the tightest applicable one (<see cref="Binding"/>) plus what approved requests added
/// (<see cref="Extra"/>). No binding = no limit.
/// </summary>
public sealed record EffectiveQuota(long? Limit, ScopeLimit? Binding, long Extra)
{
    public static readonly EffectiveQuota Unlimited = new(null, null, 0);
}
