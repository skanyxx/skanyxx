using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Skanyxx.Host.TagHelpers;

/// <summary>
/// <c>&lt;error-state title="Couldn't load agents" detail="@Model.Error"&gt;what to check&lt;/error-state&gt;</c>:
/// what failed, the server's message, and a "Try again" that reloads the page (or goes to <see cref="RetryHref"/>).
/// Never an empty list in place of a failure.
/// </summary>
[HtmlTargetElement("error-state")]
public sealed class ErrorStateTagHelper(HtmlEncoder encoder) : TagHelper
{
    public string Title { get; set; } = "Something went wrong";

    public string? Detail { get; set; }

    public string? RetryHref { get; set; }

    public bool Compact { get; set; }

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var body = (await output.GetChildContentAsync()).GetContent();
        var request = ViewContext.HttpContext.Request;
        var retry = RetryHref ?? request.PathBase + request.Path + request.QueryString;
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag; // written self-closing, it still renders its content
        output.Attributes.SetAttribute("class", Compact ? "error-state compact" : "error-state");
        output.Attributes.SetAttribute("role", "alert");
        output.Content.SetHtmlContent(
            Icon.Svg("i-alert-circle", "error-state-icon")
            + $"<h3 class=\"error-state-title\">{encoder.Encode(Title)}</h3>"
            + (string.IsNullOrWhiteSpace(body) ? "" : $"<div class=\"error-state-body\">{body}</div>")
            + (Detail is null ? "" : $"<p class=\"error-state-detail\">{encoder.Encode(Detail)}</p>")
            + $"<div class=\"error-state-actions\"><a class=\"btn btn-secondary\" href=\"{encoder.Encode(retry)}\">Try again</a></div>");
    }
}
