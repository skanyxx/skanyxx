namespace Skanyxx.Module.Runtime.Models.Definition;

/// <param name="Key">The field's name in the provider block (kagent's JSON name).</param>
/// <param name="Kind">One of <see cref="ParamKinds"/>.</param>
internal sealed record ParamField(string Key, string Kind);
