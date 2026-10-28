using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Skanyxx.Host.TagHelpers;

/// <summary>
/// <c>&lt;page-header description="…"&gt;actions&lt;/page-header&gt;</c>: the page's h1 (ViewData["Title"] unless
/// <see cref="Title"/>), one line saying what the page is for, and its actions on the right. Using it tells the layout
/// not to draw its own default header.
/// </summary>
[HtmlTargetElement("page-header")]
public sealed class PageHeaderTagHelper(HtmlEncoder encoder) : TagHelper
{
    public const string Flag = "skx:PageHeader";

    public string? Title { get; set; }

    public string? Description { get; set; }

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ViewContext.ViewData[Flag] = true;
        var actions = (await output.GetChildContentAsync()).GetContent();
        output.TagName = "header";
        output.Attributes.SetAttribute("class", "page-header");
        var title = encoder.Encode(Title ?? ViewContext.ViewData["Title"]?.ToString() ?? "");
        var description = Description is null ? "" : $"<p class=\"page-description\">{encoder.Encode(Description)}</p>";
        var right = string.IsNullOrWhiteSpace(actions) ? "" : $"<div class=\"page-actions\">{actions}</div>";
        output.Content.SetHtmlContent($"<div class=\"page-header-text\"><h1 class=\"page-title\">{title}</h1>{description}</div>{right}");
    }
}
