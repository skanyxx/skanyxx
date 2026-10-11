namespace Skanyxx.Core.Platform.Memory;

/// <summary>
/// D076/D077: the settings both sides of sandbox memory read. Memory owns them (the listener, the signing key); the
/// sandboxes module reads them only to refuse a <c>Sandboxes:MemoryMcpUrl</c> that does not point at that listener.
/// </summary>
public static class SandboxMemory
{
    /// <summary>The Kestrel port that serves <c>/mcp/memory</c> to sandboxes, identified by their token only.</summary>
    public const string PortKey = "Memory:SandboxMcpPort";

    /// <summary>The HMAC key sandbox tokens are signed with (at least 32 characters; never logged).</summary>
    public const string TokenKeyKey = "Memory:SandboxTokenKey";

    /// <summary>The shortest <see cref="TokenKeyKey"/> either side accepts.</summary>
    public const int MinTokenKeyChars = 32;

    /// <summary>The wire prefix of a sandbox token, so a leaked one is recognisable.</summary>
    public const string TokenPrefix = "skx_sbx_";
}
