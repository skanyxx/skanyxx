namespace Skanyxx.Core.Services;

/// <summary>
/// A blocking turn kagent answered with a task that did not complete (<c>failed</c>, <c>rejected</c>, <c>canceled</c>,
/// …). The message is fixed, as for every kagent failure; <see cref="Reason"/> is kagent's own status message, already
/// through <see cref="Platform.Chat.ChatFailureReason"/>, for the log and for builders and up (D553).
/// </summary>
public sealed class KAgentTaskFailedException(string? taskId, string state, string? reason)
    : HttpRequestException("the agent did not complete the answer")
{
    public string? TaskId { get; } = taskId;

    public string State { get; } = state;

    public string? Reason { get; } = reason;
}
