namespace Skanyxx.Host.Diagnostics;

/// <summary>A configuration provider, in precedence order (last wins), with how many keys it holds.</summary>
public sealed record ConfigSource(string Name, int Keys);
