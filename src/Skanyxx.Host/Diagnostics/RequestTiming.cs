namespace Skanyxx.Host.Diagnostics;

/// <summary>Timings of one method + path over the recent samples.</summary>
public sealed record RequestTiming(string Method, string Path, int Count, double MedianMs, double P95Ms, double MaxMs, int Errors);
