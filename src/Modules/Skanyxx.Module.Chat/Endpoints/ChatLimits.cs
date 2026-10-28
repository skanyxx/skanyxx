namespace Skanyxx.Module.Chat.Endpoints;

/// <summary>The upload limits, so the page checks a file before sending it (the server checks again).</summary>
internal sealed record ChatLimits(
    IReadOnlyList<string> TextExtensions, IReadOnlyList<string> ImageExtensions, int MaxTextBytes, int MaxImageBytes, int MaxFilesPerMessage);
