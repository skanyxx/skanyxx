namespace Skanyxx.Module.Agents.Plugins.Format;

/// <summary>A plugin in another git repository: its URL, the full commit SHA, and the plugin's folder in it ("" for the root).</summary>
internal sealed record PinnedGitSource(string Url, string Sha, string Subdir);
