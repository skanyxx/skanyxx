namespace Skanyxx.Core.Platform;

/// <summary>
/// A handler's result. On <see cref="OutcomeStatus.Conflict"/>, <see cref="Value"/> is the current state to re-read and
/// <see cref="Reason"/> (optional) says which kind of conflict it is.
/// </summary>
public sealed record Outcome<T>(OutcomeStatus Status, T? Value = default, string? Message = null, string? Reason = null)
{
    public static Outcome<T> Ok(T value) => new(OutcomeStatus.Ok, value);
    public static Outcome<T> Created(T value) => new(OutcomeStatus.Created, value);
    public static Outcome<T> NotFound(string message) => new(OutcomeStatus.NotFound, Message: message);
    public static Outcome<T> Forbidden(string message) => new(OutcomeStatus.Forbidden, Message: message);
    public static Outcome<T> Unauthorized(string message) => new(OutcomeStatus.Unauthorized, Message: message);
    public static Outcome<T> Conflict(T? current, string message, string? reason = null) =>
        new(OutcomeStatus.Conflict, current, message, reason);
    public static Outcome<T> RateLimited(string message) => new(OutcomeStatus.RateLimited, Message: message);
    public static Outcome<T> Accepted(T value) => new(OutcomeStatus.Accepted, value);
    public static Outcome<T> Unavailable(string message) => new(OutcomeStatus.Unavailable, Message: message);

    /// <summary>The same outcome with its value (if any) mapped, e.g. an entity to the DTO a contract returns.</summary>
    public Outcome<TOut> Map<TOut>(Func<T, TOut> map) => new(Status, Value is null ? default : map(Value), Message, Reason);
}
