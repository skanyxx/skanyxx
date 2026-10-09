namespace Skanyxx.Module.Memory.Domain;

/// <summary>The one agent repo the studio trusts (D119). A single row (<see cref="Id"/> is always 1).</summary>
public sealed class StudioRepo
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public long RepoId { get; set; }
    public required string RepoCreatedAt { get; set; }
    public required string FullName { get; set; }
    public DateTime RecordedAt { get; set; }
    public required string RecordedBy { get; set; }
}
