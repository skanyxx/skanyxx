namespace Skanyxx.Core.Platform.Plugins;

/// <summary>
/// Where a plugin came from. <see cref="Kind"/> is <c>git</c> (<see cref="Location"/> the https repo URL,
/// <see cref="Commit"/> the full SHA), <c>zip</c> (the uploaded file's name) or <c>catalog</c> (the bundled plugin's
/// name). <see cref="Path"/> is the plugin's folder inside the source; <see cref="Digest"/> the SHA-256 of what was read.
/// </summary>
public sealed record PluginSource(string Kind, string Location, string? Commit, string Path, string Digest);
