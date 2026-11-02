using Skanyxx.Core.Platform.Chat;

namespace Skanyxx.Module.Investigate.Agents;

/// <summary>
/// No usable answer came back. The message is a fixed category, never kagent's or the model's own text. When kagent
/// ended the task itself (failed, rejected, canceled), <see cref="KagentReason"/> is its status message, cleaned by
/// <c>ChatFailureReason</c>, for the log and builders and up (D556), and <see cref="Usage"/> the tokens kagent reported
/// for the failed turn: they were spent (D551).
/// </summary>
public sealed class AgentCallException(
    string message, string? taskId = null, string? state = null, string? kagentReason = null, ChatUsage? usage = null) : Exception(message)
{
    public ChatUsage Usage { get; } = usage ?? ChatUsage.None;

    public string? TaskId { get; } = taskId;

    public string? State { get; } = state;

    public string? KagentReason { get; } = kagentReason;
}
