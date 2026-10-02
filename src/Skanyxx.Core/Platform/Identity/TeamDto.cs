namespace Skanyxx.Core.Platform.Identity;

/// <summary><paramref name="Members"/> are user ids.</summary>
public sealed record TeamDto(string Slug, string Name, string Department, IReadOnlyList<string> Members);
