namespace Skanyxx.Core.Platform.Usage;

/// <summary>
/// Skanyxx's own count of the tokens it sends a model (D548): never the provider's, which kagent reports. One estimator
/// for every place that needs a count before or without the model's, so a per-model tokenizer can replace it in one place.
/// </summary>
public interface ITokenEstimator
{
    /// <param name="model">The ModelConfig (<c>namespace/name</c>) the text goes to; null when unknown. The default ignores it.</param>
    long Count(string text, string? model = null);

    /// <summary>Bytes sent as base64 (an attached image): counted as the characters actually on the wire.</summary>
    long CountBase64(long bytes, string? model = null);
}
