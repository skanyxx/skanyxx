namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>
/// The status line: <see cref="Code"/> is <c>ready</c>, <c>unknown</c> (kagent unreachable) or the first fix's code;
/// <see cref="Text"/> says it in plain words.
/// </summary>
public sealed record ServerStatus(string Code, string Text)
{
    public const string Ready = "ready";
    public const string Unknown = "unknown";
}
