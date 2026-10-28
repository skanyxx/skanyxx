using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Skanyxx.Host.TagHelpers;

/// <summary>
/// <c>&lt;status-badge status="Ready" /&gt;</c>: the status word with a dot in its <see cref="StatusTone"/> colour.
/// <see cref="Label"/> overrides the text, <see cref="Tone"/> the colour.
/// </summary>
[HtmlTargetElement("status-badge", TagStructure = TagStructure.WithoutEndTag)]
public sealed class StatusBadgeTagHelper(HtmlEncoder encoder) : TagHelper
{
    public string? Status { get; set; }

    public string? Label { get; set; }

    public string? Tone { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var tone = Tone ?? StatusTone.Of(Status);
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", $"badge badge-{tone}");
        output.Content.SetHtmlContent($"<span class=\"badge-dot\" aria-hidden=\"true\"></span>{encoder.Encode(Label ?? Status ?? "Unknown")}");
    }
}
