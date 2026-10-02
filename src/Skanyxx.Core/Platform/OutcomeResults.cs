using Microsoft.AspNetCore.Http;

namespace Skanyxx.Core.Platform;

public static class OutcomeResults
{
    /// <summary>The HTTP status an outcome answers with, for callers that render their own body (Razor pages).</summary>
    public static int HttpStatus(this OutcomeStatus status) => status switch
    {
        OutcomeStatus.Ok => StatusCodes.Status200OK,
        OutcomeStatus.Created => StatusCodes.Status201Created,
        OutcomeStatus.NotFound => StatusCodes.Status404NotFound,
        OutcomeStatus.Forbidden => StatusCodes.Status403Forbidden,
        OutcomeStatus.Unauthorized => StatusCodes.Status401Unauthorized,
        OutcomeStatus.Conflict => StatusCodes.Status409Conflict,
        OutcomeStatus.RateLimited => StatusCodes.Status429TooManyRequests,
        OutcomeStatus.Accepted => StatusCodes.Status202Accepted,
        OutcomeStatus.Unavailable => StatusCodes.Status502BadGateway,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    /// <summary>Statuses come from <see cref="HttpStatus"/>, so pages and endpoints cannot drift apart.</summary>
    public static IResult ToHttp<T, TDto>(this Outcome<T> outcome, Func<T, TDto> map) => outcome.Status switch
    {
        OutcomeStatus.Ok => Results.Ok(map(outcome.Value!)),
        OutcomeStatus.Created => Results.Json(map(outcome.Value!), statusCode: outcome.Status.HttpStatus()),
        OutcomeStatus.Conflict => Results.Json(
            new ConflictResponse<TDto>(outcome.Message!, outcome.Value is null ? default : map(outcome.Value)),
            statusCode: outcome.Status.HttpStatus()),
        OutcomeStatus.NotFound or OutcomeStatus.Forbidden or OutcomeStatus.Unauthorized or OutcomeStatus.RateLimited or OutcomeStatus.Unavailable =>
            Results.Problem(outcome.Message, statusCode: outcome.Status.HttpStatus()),
        OutcomeStatus.Accepted => Results.Json(map(outcome.Value!), statusCode: outcome.Status.HttpStatus()),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Status, null)
    };
}
