using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Skanyxx.Host.TagHelpers;

/// <summary>
/// <c>&lt;alert tone="error|warning|success|info" title="…"&gt;text&lt;/alert&gt;</c>: an inline message that stays
/// on the page. Errors and warnings are announced (role=alert), success and info politely (role=status).
/// </summary>
[HtmlTargetElement("alert")]
public sealed class AlertTagHelper(HtmlEncoder encoder) : TagHelper
{
    public string Tone { get; set; } = "info";

    public string? Title { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = (await output.GetChildContentAsync()).GetContent();
        var tone = Tone is "error" or "warning" or "success" ? Tone : "info";
        output.TagName = "div";
        output.Attributes.SetAttribute("class", $"alert alert-{tone}");
        output.Attributes.SetAttribute("role", tone is "error" or "warning" ? "alert" : "status");
        var icon = tone switch { "error" => "i-alert-circle", "warning" => "i-alert-triangle", "success" => "i-check-circle", _ => "i-info" };
        var title = Title is null ? "" : $"<div class=\"alert-title\">{encoder.Encode(Title)}</div>";
        output.Content.SetHtmlContent(Icon.Svg(icon) + $"<div class=\"alert-body\">{title}{body}</div>");
    }
}
