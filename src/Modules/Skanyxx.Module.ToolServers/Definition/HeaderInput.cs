namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>
/// One header of the form. <see cref="HeaderSources.Value"/>: <see cref="Value"/> goes to kagent once, into a Secret kagent
/// creates with the server; Skanyxx never stores, logs or returns it. <see cref="HeaderSources.Secret"/>: kagent reads
/// <see cref="SecretKey"/> of an existing Secret <see cref="SecretName"/> in the server's namespace.
/// </summary>
public sealed class HeaderInput
{
    public string? Name { get; set; }

    public string? Source { get; set; }

    public string? Value { get; set; }

    public string? SecretName { get; set; }

    public string? SecretKey { get; set; }
}
