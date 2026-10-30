namespace Skanyxx.Core.Platform.Usage;

/// <summary>What one million tokens cost on a model, in and out, as the owner configured it.</summary>
public sealed class ModelPrice
{
    public decimal InputPerMillion { get; set; }

    public decimal OutputPerMillion { get; set; }
}
