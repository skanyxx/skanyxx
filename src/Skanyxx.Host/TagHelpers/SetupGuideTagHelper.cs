using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Skanyxx.Host.TagHelpers;

/// <summary>
/// <c>&lt;setup-guide id="guide-models" title="Connect a model" summary="…" done="1" total="3"&gt;&lt;ol class="steps"&gt;…&lt;/ol&gt;&lt;/setup-guide&gt;</c>:
/// a collapsible panel of numbered steps for people who have not done this before. Open until every step is done;
/// ui.js remembers when someone closes it (per browser, by <c>id</c>).
/// </summary>
[HtmlTargetElement("setup-guide")]
public sealed class SetupGuideTagHelper(HtmlEncoder encoder) : TagHelper
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string? Summary { get; set; }

    public int? Done { get; set; }

    public int? Total { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var steps = (await output.GetChildContentAsync()).GetContent();
        var complete = Done is { } d && Total is { } t && d >= t;
        output.TagName = "details";
        output.Attributes.SetAttribute("class", "setup-guide");
        output.Attributes.SetAttribute("id", Id);
        output.Attributes.SetAttribute("data-guide", Id);
        if (!complete)
            output.Attributes.SetAttribute("open", "open");
        var progress = Done is { } done && Total is { } total ? $"<span class=\"setup-guide-progress\">{done} of {total} done</span>" : "";
        var summary = Summary is null ? "" : $"<span class=\"setup-guide-summary\">{encoder.Encode(Summary)}</span>";
        output.Content.SetHtmlContent(
            $"<summary>{Icon.Svg("i-compass", "setup-guide-icon")}"
            + $"<span class=\"setup-guide-heading\"><span class=\"setup-guide-title\">{encoder.Encode(Title)}</span><br />{summary}</span>"
            + progress + Icon.Svg("i-chevron-down", "icon setup-guide-chevron") + "</summary>"
            + $"<div class=\"setup-guide-body\">{steps}</div>");
    }
}
