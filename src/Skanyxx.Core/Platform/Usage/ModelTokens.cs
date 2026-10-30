namespace Skanyxx.Core.Platform.Usage;

/// <summary>Model tokens spent on one model, as a cost is worked out from them: input and output are priced apart.</summary>
public readonly record struct ModelTokens(string Model, long Input, long Output);
