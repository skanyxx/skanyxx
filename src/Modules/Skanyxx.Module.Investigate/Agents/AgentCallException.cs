namespace Skanyxx.Module.Investigate.Agents;

/// <summary>
/// No usable answer came back. The message is a fixed category, never kagent's or the model's own text. When kagent
/// ended the task itself (failed, rejected, canceled), <see cref="KagentReason"/> is its status message, cleaned by
/// <c>ChatFailureReason</c>, for the log and builders and up (D556).
/// </summary>
public sealed class AgentCallException(string message, string? taskId = null, string? state = null, string? kagentReason = null) : Exception(message)
{
    public string? TaskId { get; } = taskId;

    public string? State { get; } = state;

    public string? KagentReason { get; } = kagentReason;
}
