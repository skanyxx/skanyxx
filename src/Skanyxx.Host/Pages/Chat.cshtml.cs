using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Skanyxx.Host.Pages;

/// <summary>
/// Chat, addressed by what is open: <c>/Chat/{namespace}/{agent}</c> picks the agent (the Agents page links there) and
/// <c>/Chat/{namespace}/{agent}/{session}</c> reopens a conversation, so a reload keeps it. The route only says what to
/// open; the API decides whether the caller may (another person's session is 404 there). Anything not shaped like a
/// Kubernetes name or a session id is no page at all.
/// </summary>
public sealed partial class ChatModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Namespace { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Agent { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Session { get; set; }

    public IActionResult OnGet() =>
        Valid(Namespace, KubeName()) && Valid(Agent, KubeName()) && Valid(Session, SessionId()) && (Namespace is null) == (Agent is null)
            ? Page()
            : NotFound();

    private static bool Valid(string? value, Regex shape) => value is null || shape.IsMatch(value);

    [GeneratedRegex("^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?$")]
    private static partial Regex KubeName();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex SessionId();
}
