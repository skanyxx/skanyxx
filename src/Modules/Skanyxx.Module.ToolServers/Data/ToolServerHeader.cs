namespace Skanyxx.Module.ToolServers.Data;

/// <summary>
/// A header kagent sends to the server, read from <paramref name="SecretKey"/> of the Secret <paramref name="SecretName"/>.
/// <paramref name="Companion"/>: kagent created that Secret with the server (from a value typed on the page) and deletes it
/// with the server; otherwise it is a Secret someone else keeps.
/// </summary>
public sealed record ToolServerHeader(string Name, string SecretName, string SecretKey, bool Companion);
