namespace Skanyxx.Module.Chat.Features;

/// <summary>What a person sees when kagent fails: fixed texts only; kagent's own text and the cause go to the log.</summary>
internal static class ChatFailure
{
    public const string Generic = "The agents cannot answer right now. Try again in a moment.";

    public static string Message(Exception ex) => ex is TimeoutException ? "The agent did not answer in time. Try again, or ask something shorter." : Generic;
}
