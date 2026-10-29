namespace Skanyxx.Core.Platform.Studio;

/// <summary>What CI is told about one agent folder (D362): valid or not, and why, in the studio's words.</summary>
public sealed record AgentFilesCheck(bool Valid, IReadOnlyList<string> Problems, IReadOnlyList<string> Notes);
