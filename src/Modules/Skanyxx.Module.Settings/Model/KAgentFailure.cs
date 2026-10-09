namespace Skanyxx.Module.Settings.Model;

internal static class KAgentFailure
{
    /// <summary>kagent refused, answered garbage, or missed its deadline (<see cref="TimeoutException"/>).</summary>
    public static bool Is(Exception ex) => ex is HttpRequestException or TimeoutException;

    // Owner-only path: KAgentApiClient's fixed texts plus kagent's own message are shown. A connection failure's text
    // can carry the controller address, so only its type is shown.
    public static string Message(Exception ex) => ex switch
    {
        TimeoutException => ex.Message,
        HttpRequestException { StatusCode: not null } => $"kagent: {ex.Message}",
        _ => $"kagent is not reachable ({ex.GetType().Name})."
    };
}
