namespace Skanyxx.Host.Diagnostics;

/// <summary>One configuration key as the Debug page shows it: the value (or <c>[redacted]</c>) and the source that supplied it.</summary>
public sealed record ConfigRow(string Key, string Value, string Source);
