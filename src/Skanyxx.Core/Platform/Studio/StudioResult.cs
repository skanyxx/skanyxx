namespace Skanyxx.Core.Platform.Studio;

/// <summary>What a studio action did, in words for the person, plus the proposal and agent it was about.</summary>
public sealed record StudioResult(string Message, int? Number, string? Agent);
