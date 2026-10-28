using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Skanyxx.Host.TagHelpers;

/// <summary>
/// <c>&lt;copy-button value="…" /&gt;</c> or <c>&lt;copy-button target="#inviteLink" /&gt;</c>: copies the value (or
/// the target field's value) and says "Copied" (ui.js). Never put a secret in <see cref="Value"/>: it is in the HTML.
/// </summary>
[HtmlTargetElement("copy-button", TagStructure = TagStructure.WithoutEndTag)]
public sealed class CopyButtonTagHelper(HtmlEncoder encoder) : TagHelper
{
    public string? Value { get; set; }

    public string? Target { get; set; }

    public string Label { get; set; } = "Copy";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "button";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("type", "button");
        output.Attributes.SetAttribute("class", "btn btn-secondary copy-btn");
        if (Value is not null)
            output.Attributes.SetAttribute("data-copy", Value);
        if (Target is not null)
            output.Attributes.SetAttribute("data-copy-target", Target);
        output.Content.SetHtmlContent(Icon.Svg("i-copy") + $"<span data-copy-label>{encoder.Encode(Label)}</span>");
    }
}
