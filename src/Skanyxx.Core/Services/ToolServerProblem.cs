namespace Skanyxx.Core.Services;

/// <param name="Server"><c>namespace/name</c> of the tool server.</param>
/// <param name="Problem">One of <see cref="ToolServerHealth"/>'s fixed texts.</param>
public sealed record ToolServerProblem(string Server, string Problem);
