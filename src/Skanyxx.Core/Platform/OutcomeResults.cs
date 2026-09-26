using Microsoft.AspNetCore.Http;

namespace Skanyxx.Core.Platform;

public static class OutcomeResults
{
    public static IResult ToHttp<T, TDto>(this Outcome<T> outcome, Func<T, TDto> map) => outcome.Status switch
    {
        OutcomeStatus.Ok => Results.Ok(map(outcome.Value!)),
        OutcomeStatus.Created => Results.Json(map(outcome.Value!), statusCode: StatusCodes.Status201Created),
        OutcomeStatus.Conflict => Results.Json(
            new ConflictResponse<TDto>(outcome.Message!, outcome.Value is null ? default : map(outcome.Value)),
            statusCode: StatusCodes.Status409Conflict),
        OutcomeStatus.NotFound => Results.Problem(outcome.Message, statusCode: StatusCodes.Status404NotFound),
        OutcomeStatus.Forbidden => Results.Problem(outcome.Message, statusCode: StatusCodes.Status403Forbidden),
        OutcomeStatus.RateLimited => Results.Problem(outcome.Message, statusCode: StatusCodes.Status429TooManyRequests),
        OutcomeStatus.Accepted => Results.Json(map(outcome.Value!), statusCode: StatusCodes.Status202Accepted),
        OutcomeStatus.Unavailable => Results.Problem(outcome.Message, statusCode: StatusCodes.Status502BadGateway),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Status, null)
    };
}
