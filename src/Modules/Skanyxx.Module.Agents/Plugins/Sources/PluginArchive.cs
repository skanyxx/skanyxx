namespace Skanyxx.Module.Agents.Plugins.Sources;

/// <summary>What a source read: its files and the SHA-256 (lowercase hex) that identifies exactly what was read (P12).</summary>
internal sealed record PluginArchive(PluginTree Tree, string Digest);
