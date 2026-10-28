using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Skanyxx.Host.TagHelpers;

/// <summary>
/// <c>&lt;field for="f-name" label="Name" hint="Lowercase letters…" error="@Model.NameError"&gt;&lt;input id="f-name" …/&gt;&lt;/field&gt;</c>:
/// label, control, hint and an inline error slot (<c>{for}-hint</c>, <c>{for}-error</c>). ui.js points the control's
/// aria-describedby at both and sets aria-invalid while the error has text; <c>skx.fieldError(input, text)</c> fills it.
/// </summary>
[HtmlTargetElement("field")]
public sealed class FieldTagHelper(HtmlEncoder encoder) : TagHelper
{
    public string For { get; set; } = "";

    public string Label { get; set; } = "";

    public string? Hint { get; set; }

    public string? Error { get; set; }

    public bool Optional { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var control = (await output.GetChildContentAsync()).GetContent();
        var id = encoder.Encode(For);
        output.TagName = "div";
        output.Attributes.SetAttribute("class", "field");
        output.Attributes.SetAttribute("data-field", For);
        var optional = Optional ? " <span class=\"field-optional\">(optional)</span>" : "";
        output.Content.SetHtmlContent(
            $"<label for=\"{id}\">{encoder.Encode(Label)}{optional}</label>"
            + control
            + (Hint is null ? "" : $"<p class=\"field-hint\" id=\"{id}-hint\">{encoder.Encode(Hint)}</p>")
            + $"<p class=\"field-error\" id=\"{id}-error\" aria-live=\"polite\">{encoder.Encode(Error ?? "")}</p>");
    }
}
