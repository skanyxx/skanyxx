namespace Skanyxx.Host.TagHelpers;

/// <summary>An icon from the layout's sprite (Pages/Shared/_NavIcons.cshtml), decorative: the text next to it names the thing.</summary>
internal static class Icon
{
    public static string Svg(string id, string? cssClass = "icon") =>
        $"<svg class=\"{cssClass}\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#{id}\"/></svg>";
}
