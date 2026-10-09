using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Agents.Studio.Features;

/// <summary>Fixed texts for people; what git or kagent said goes to the log only.</summary>
internal static class StudioFailure
{
    public const string NotConfigured = "The studio needs the agent repo: set Studio:Git:BaseUrl and Studio:Git:Token (README \"Studio\").";
    public const string Unavailable = "The agent repo or kagent cannot be reached right now. Try again in a moment.";
    public const string NotStudio = "Only builders and supervisors use the studio.";
    public const string NotBuilder = "Only a builder (or the owner) proposes agents.";
    public const string NotSupervisor = "Only a supervisor (or the owner) merges agent proposals.";
    public const string NoProposal = "No such open proposal.";
    public const string NotOwner = "Only the owner does this.";

    /// <summary>The machine reason of every repo-guard refusal (D119): the repo, not the request, needs a person.</summary>
    public const string RepoReason = "repo";

    public static string NameTaken(string agent) =>
        $"Memory already has an agent '{agent}' that the studio did not create (the owner's, or an external client's); the studio never takes it over. Choose another name.";

    public static Outcome<T> Conflict<T>(string message, string? reason = null) => Outcome<T>.Conflict(default, message, reason);
}
