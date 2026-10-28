using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Skanyxx.Module.Analytics.Health;

/// <summary>A registered health check's status and its own data (counts, times). Its description and exception are dropped.</summary>
public sealed record CheckFact(HealthStatus Status, IReadOnlyDictionary<string, object> Data);
