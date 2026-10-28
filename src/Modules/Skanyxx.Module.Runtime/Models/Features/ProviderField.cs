namespace Skanyxx.Module.Runtime.Models.Features;

/// <param name="Kind">One of <c>ParamKinds</c>: text, integer or boolean.</param>
internal sealed record ProviderField(string Key, string Kind, bool Required);
