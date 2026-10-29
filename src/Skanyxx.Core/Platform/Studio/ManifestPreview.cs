namespace Skanyxx.Core.Platform.Studio;

/// <summary>The two files a form would put in git (D361), and every reason it could not be proposed yet, in plain words.</summary>
public sealed record ManifestPreview(string AgentYaml, string GrantsYaml, IReadOnlyList<string> Problems);
