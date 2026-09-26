namespace Skanyxx.Module.Tickets;

/// <summary>Read-only Jira access. The token is never returned by any endpoint and never logged.</summary>
public sealed class JiraOptions
{
    public string Site { get; set; } = "";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string Project { get; set; } = "";

    /// <summary>Overrides <see cref="Project"/> when set.</summary>
    public string Jql { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 20;

    public bool CredentialConfigured => Email.Length > 0 && ApiToken.Length > 0;
}
