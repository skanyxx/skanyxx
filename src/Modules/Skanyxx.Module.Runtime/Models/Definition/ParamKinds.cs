namespace Skanyxx.Module.Runtime.Models.Definition;

/// <summary>How a provider field is typed in the CRD, so a form value is sent as kagent decodes it (or refused).</summary>
internal static class ParamKinds
{
    public const string Text = "text";
    public const string Integer = "integer";
    public const string Boolean = "boolean";
}
