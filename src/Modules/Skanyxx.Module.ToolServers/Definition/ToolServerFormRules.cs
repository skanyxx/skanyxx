using System.Net;
using System.Text.RegularExpressions;

namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>What the form must be before anything reaches kagent. A problem never quotes a header value.</summary>
public static partial class ToolServerFormRules
{
    public const string DefaultNamespace = "kagent";
    public const int MaxHeaders = 10;
    public const int MaxHeaderValue = 8192;
    public const int MaxDescription = 500;
    public const int MaxTimeoutSeconds = 600;
    public const int MaxUrl = 2048;

    public const string BadName = "Name and namespace must be lowercase letters, digits and hyphens (at most 63, starting and ending with a letter or digit).";

    public static IReadOnlyList<string> Problems(ToolServerForm form)
    {
        var problems = new List<string>();
        if (!IsDnsLabel(form.Name) || !IsDnsLabel(form.Namespace))
            problems.Add(BadName);
        if (UrlProblem(form.Url) is { } url)
            problems.Add(url);
        if (form.Protocol is not null && !ToolServerProtocols.All.Contains(form.Protocol))
            problems.Add($"The protocol is {ToolServerProtocols.StreamableHttp} or {ToolServerProtocols.Sse}.");
        if (form.TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
            problems.Add($"The timeout is 1 to {MaxTimeoutSeconds} seconds (empty: kagent's 30 s).");
        if (form.Description?.Length > MaxDescription)
            problems.Add($"The description is at most {MaxDescription} characters.");
        if (form.Headers.Count > MaxHeaders)
            problems.Add($"At most {MaxHeaders} headers.");
        if (form.Headers.Any(h => h is null))
            return [.. problems, "Each header has a name and a value or Secret."];
        foreach (var header in form.Headers)
            if (HeaderProblem(header) is { } problem)
                problems.Add(problem);
        if (form.Headers.Select(h => h.Name?.ToLowerInvariant()).Distinct().Count() < form.Headers.Count)
            problems.Add("Each header is named once.");
        return problems;
    }

    public static bool IsDnsLabel(string? value) => value is not null && DnsLabel().IsMatch(value);

    /// <summary>Null for an absolute http(s) URL that carries no credential; else why not.</summary>
    public static string? UrlProblem(string? value)
    {
        if (value is null || value.Length > MaxUrl || !Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            return "The URL must be an absolute http or https address (kagent's controller and agent pods call it).";
        // The spec is plain text in the cluster: a credential in the URL is readable by anyone who reads the resource.
        if (url.UserInfo.Length > 0)
            return "The URL must not carry a user name or password: send it as a header from a Secret.";
        if (SecretQuery().IsMatch(url.Query))
            return "The URL must not carry a key, token, secret or password in its query: send it as a header from a Secret.";
        return null;
    }

    /// <summary>A host that, seen from a pod, is the pod itself: kagent cannot reach a server on the developer's machine there.</summary>
    public static bool IsLoopback(Uri url) =>
        url.IsLoopback || (IPAddress.TryParse(url.Host.Trim('[', ']'), out var ip) && (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)));

    private static string? HeaderProblem(HeaderInput header)
    {
        if (header.Name is null || !HeaderName().IsMatch(header.Name))
            return "A header name is letters, digits and hyphens (at most 64), e.g. Authorization or X-Api-Key.";
        return header.Source switch
        {
            HeaderSources.Value when string.IsNullOrEmpty(header.Value) =>
                $"Header {header.Name}: enter its value (Skanyxx never shows a stored value, so an edit needs it again), or read it from a Secret.",
            HeaderSources.Value when header.Value.Length > MaxHeaderValue => $"Header {header.Name}: the value is at most {MaxHeaderValue} characters.",
            HeaderSources.Value when header.Value.Any(c => c is '\r' or '\n') => $"Header {header.Name}: the value is one line.",
            HeaderSources.Value => null,
            HeaderSources.Secret when header.SecretName is null || header.SecretName.Length > 253 || !SecretName().IsMatch(header.SecretName) =>
                $"Header {header.Name}: the Secret name is lowercase letters, digits, '-' and '.' (a Kubernetes name).",
            HeaderSources.Secret when header.SecretKey is null || header.SecretKey.Length > 253 || !SecretKey().IsMatch(header.SecretKey) =>
                $"Header {header.Name}: the Secret key is letters, digits, '-', '_' and '.'.",
            HeaderSources.Secret => null,
            _ => $"Header {header.Name}: its value is typed here or read from a Secret."
        };
    }

    // \z, not $: in .NET, $ also matches before a trailing "\n".
    [GeneratedRegex(@"^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?\z")]
    private static partial Regex DnsLabel();

    [GeneratedRegex(@"^[a-z0-9]([-a-z0-9]*[a-z0-9])?(\.[a-z0-9]([-a-z0-9]*[a-z0-9])?)*\z")]
    private static partial Regex SecretName();

    [GeneratedRegex(@"^[-._a-zA-Z0-9]+\z")]
    private static partial Regex SecretKey();

    [GeneratedRegex(@"^[A-Za-z0-9][-A-Za-z0-9]{0,63}\z")]
    private static partial Regex HeaderName();

    [GeneratedRegex(@"[?&][^=&]*(key|token|secret|password|auth)[^=&]*=", RegexOptions.IgnoreCase)]
    private static partial Regex SecretQuery();
}
