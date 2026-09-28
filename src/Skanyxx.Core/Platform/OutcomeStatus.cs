namespace Skanyxx.Core.Platform;

public enum OutcomeStatus
{
    Ok,
    Created,
    NotFound,
    Forbidden,

    /// <summary>The caller could not be authenticated (e.g. a failed sign-in); always a generic message.</summary>
    Unauthorized,
    Conflict,
    RateLimited,
    Accepted,

    /// <summary>An upstream the handler depends on (e.g. Jira) could not answer.</summary>
    Unavailable
}
