namespace Skanyxx.Module.Chat.Features;

/// <summary>
/// What a person sees when kagent fails: fixed texts only; kagent's own text and the cause go to the log, and a failed
/// task's reason also to builders and up, beside these words (D553).
/// </summary>
internal static class ChatFailure
{
    public const string Generic = "The agents cannot answer right now. Try again in a moment.";

    public const string Unreachable = "The agents cannot answer right now: Skanyxx cannot reach kagent, the service that runs them.";

    public const string Timeout = "The agent did not answer in time. Try again, or ask something shorter.";

    public const string Unfinished = "The agent could not finish the answer. Try again in a moment.";

    public const string Empty = "The agent answered with no text. Try again, or ask in other words.";

    public const string NeedsApproval = "The agent stopped to ask for an approval, which Chat cannot give yet. Ask a supervisor to turn approvals off for this agent's tools.";

    public static string Message(Exception ex) => ex switch
    {
        TimeoutException => Timeout,
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError } => Unreachable,
        _ => Generic
    };
}
