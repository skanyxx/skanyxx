namespace Skanyxx.Module.Memory.Domain;

/// <summary>
/// Who is asking. TODO(identity-slice): both ids come from request headers and are not authenticated.
/// </summary>
public sealed record Caller(string? UserId, string? AgentId)
{
    public bool IsAgent => AgentId is not null;

    public Scope? PersonalScope => UserId is null ? null : Scope.Personal(UserId);

    public string Who => (UserId, AgentId) switch
    {
        ({ } user, { } agent) => $"{user} via {agent}",
        (null, { } agent) => $"agent:{agent}",
        ({ } user, null) => user,
        _ => throw new InvalidOperationException("A caller without identity reached a write; validation should have rejected it.")
    };

    public string RateLimitKey => AgentId is null ? $"user:{UserId}" : $"agent:{AgentId}";
}
