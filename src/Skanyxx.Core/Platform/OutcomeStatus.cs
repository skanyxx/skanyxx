namespace Skanyxx.Core.Platform;

public enum OutcomeStatus
{
    Ok,
    Created,
    NotFound,
    Forbidden,
    Conflict,
    RateLimited,
    Accepted,

    /// <summary>An upstream the handler depends on (e.g. Jira) could not answer.</summary>
    Unavailable
}
