namespace Skanyxx.Module.Agents.Studio.Git;

/// <summary>git answered more than the caller allows; nothing past the limit was read.</summary>
internal sealed class RepoFileTooLargeException(int maxBytes) : Exception($"git answered more than {maxBytes} bytes.");
