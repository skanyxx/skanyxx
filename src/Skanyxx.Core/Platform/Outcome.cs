namespace Skanyxx.Core.Platform;

/// <summary>A handler's result. On <see cref="OutcomeStatus.Conflict"/>, <see cref="Value"/> is the current state to re-read.</summary>
public sealed record Outcome<T>(OutcomeStatus Status, T? Value = default, string? Message = null)
{
    public static Outcome<T> Ok(T value) => new(OutcomeStatus.Ok, value);
    public static Outcome<T> Created(T value) => new(OutcomeStatus.Created, value);
    public static Outcome<T> NotFound(string message) => new(OutcomeStatus.NotFound, Message: message);
    public static Outcome<T> Forbidden(string message) => new(OutcomeStatus.Forbidden, Message: message);
    public static Outcome<T> Conflict(T? current, string message) => new(OutcomeStatus.Conflict, current, message);
    public static Outcome<T> RateLimited(string message) => new(OutcomeStatus.RateLimited, Message: message);
    public static Outcome<T> Accepted(T value) => new(OutcomeStatus.Accepted, value);
    public static Outcome<T> Unavailable(string message) => new(OutcomeStatus.Unavailable, Message: message);
}
