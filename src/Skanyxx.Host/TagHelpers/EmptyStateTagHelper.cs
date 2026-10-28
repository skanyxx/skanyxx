using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Skanyxx.Host.TagHelpers;

/// <summary>
/// <c>&lt;empty-state icon="i-bot" title="No agents yet"&gt;what this is and the first step&lt;/empty-state&gt;</c>.
/// An empty list teaches: say what would be here and how to get the first one (a link or button in the content).
/// </summary>
[HtmlTargetElement("empty-state")]
public sealed class EmptyStateTagHelper(HtmlEncoder encoder) : TagHelper
{
    public string Icon { get; set; } = "i-inbox";

    public string Title { get; set; } = "";

    public bool Compact { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = (await output.GetChildContentAsync()).GetContent();
        output.TagName = "div";
        output.Attributes.SetAttribute("class", Compact ? "empty-state compact" : "empty-state");
        output.Content.SetHtmlContent(
            TagHelpers.Icon.Svg(Icon, "empty-state-icon")
            + $"<h3 class=\"empty-state-title\">{encoder.Encode(Title)}</h3>"
            + (string.IsNullOrWhiteSpace(body) ? "" : $"<div class=\"empty-state-body\">{body}</div>"));
    }
}
