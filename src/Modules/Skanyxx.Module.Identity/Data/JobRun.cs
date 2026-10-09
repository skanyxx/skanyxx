namespace Skanyxx.Module.Identity.Data;

/// <summary>When a background job last ran across every replica (D164): read and written under the job's advisory lock.</summary>
public sealed class JobRun
{
    public string Name { get; set; } = "";
    public DateTimeOffset LastRunUtc { get; set; }
}
