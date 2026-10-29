namespace Skanyxx.Host.Diagnostics;

/// <summary>One finished request: method, path (no query), status and how long it took.</summary>
public sealed record RequestSample(string Method, string Path, int Status, double Milliseconds);
