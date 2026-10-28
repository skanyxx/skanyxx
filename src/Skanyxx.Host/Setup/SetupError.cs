namespace Skanyxx.Host.Setup;

/// <summary>A failure people meet while setting a feature up, and what fixes it.</summary>
public sealed record SetupError(string Symptom, string Fix);
