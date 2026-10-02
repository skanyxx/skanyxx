namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

/// <summary>How the fake kagent answers one call.</summary>
public abstract record AgentReply
{
    public sealed record Completed(string Text) : AgentReply;
    public sealed record Failed(string Text) : AgentReply;
    public sealed record Http(int Status) : AgentReply;

    /// <summary>A body sent verbatim — for replies of an unexpected shape.</summary>
    public sealed record Raw(string Json) : AgentReply;

    /// <summary>Holds the call open until <paramref name="Release"/> completes (a stage "in flight").</summary>
    public sealed record Blocked(TaskCompletionSource Release, string Text) : AgentReply;
}
