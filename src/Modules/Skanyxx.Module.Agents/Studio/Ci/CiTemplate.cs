namespace Skanyxx.Module.Agents.Studio.Ci;

/// <summary>
/// The agent repo's CI workflow (D362): <c>docs/templates/agents-repo/validate-agents.yaml</c>, embedded at build so the
/// Studio page and its setup guide show exactly the committed file.
/// </summary>
internal static class CiTemplate
{
    public const string RepoPath = ".gitea/workflows/validate-agents.yaml";

    public static string Text { get; } = Read();

    private static string Read()
    {
        using var stream = typeof(CiTemplate).Assembly.GetManifestResourceStream("studio/validate-agents.yaml")
            ?? throw new InvalidOperationException("The CI template is not embedded in the agents module.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
