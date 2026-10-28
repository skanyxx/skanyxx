namespace Skanyxx.Core.Models;

/// <summary>A RemoteMCPServer (streamable HTTP) to create in kagent: <c>namespace/name</c> and the server's URL.</summary>
public sealed record CreateToolServerRequest(string? Name, string? Namespace, string? Url);
