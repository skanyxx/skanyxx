using System.Net;
using Serilog;

namespace Skanyxx.Host;

/// <summary>
/// Remote mode (D201): the desktop launcher. No database, no modules, no sign-in — a GET or HEAD on any path answers
/// 302 to <c>Skanyxx:RemoteUrl</c> itself (never the request's path or query, so it cannot be used as an open
/// redirect), <c>/health</c> says it is remote, everything else is 404. With no RemoteUrl, <c>/</c> explains where to set it.
/// </summary>
public static class RemoteClient
{
    public const string RemoteUrlKey = "Skanyxx:RemoteUrl";

    public static async Task RunAsync(WebApplicationBuilder builder)
    {
        var remote = ReadRemoteUrl(builder.Configuration);
        var app = builder.Build();
        app.UseSerilogRequestLogging();

        var target = remote?.AbsoluteUri;
        var instructions = InstructionsPage(Path.Combine(builder.Environment.ContentRootPath, "appsettings.json"));
        app.Run(context =>
        {
            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
                return NotFound(context);
            if (request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { mode = "remote" }).ExecuteAsync(context);
            if (target is not null)
            {
                context.Response.Redirect(target);
                return Task.CompletedTask;
            }
            return request.Path == "/"
                ? Results.Content(instructions, "text/html; charset=utf-8").ExecuteAsync(context)
                : NotFound(context);
        });

        if (remote is null)
            Log.Warning("Remote mode without {Key}: the launcher only shows where to set the company's Skanyxx address", RemoteUrlKey);
        else
            Log.Information("Remote mode: every page opens {RemoteUrl}", target);
        await app.RunAsync();
    }

    /// <summary>
    /// Empty = not set yet. Otherwise taken exactly as written: absolute, https (http only to a loopback host, the same
    /// rule as the chart's publicUrl, D145), no query, fragment, user info, whitespace or backslashes, and ASCII only:
    /// the address goes out as a Location header, which Kestrel refuses to send with a non-ASCII byte (an
    /// internationalised host is written in its <c>xn--</c> form, as the Windows installer already requires).
    /// </summary>
    public static Uri? ReadRemoteUrl(IConfiguration configuration)
    {
        var value = configuration[RemoteUrlKey];
        if (string.IsNullOrEmpty(value))
            return null;
        if (!value.Any(c => !char.IsAscii(c) || char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\')
            && Uri.TryCreate(value, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback))
            && url.Query.Length == 0 && url.Fragment.Length == 0 && url.UserInfo.Length == 0)
            return url;
        throw new InvalidOperationException(
            $"{RemoteUrlKey} must be empty or an absolute https:// URL written exactly (no spaces, backslashes, query, fragment " +
            "or user info, ASCII only — an internationalised host in its xn-- form), e.g. https://skanyxx.example.com; " +
            "http only for a loopback host such as http://localhost:8080.");
    }

    private static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    private static string InstructionsPage(string configPath) => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>Skanyxx</title></head>
        <body style="font-family: system-ui, sans-serif; max-width: 40rem; margin: 3rem auto; line-height: 1.5">
        <h1>Skanyxx</h1>
        <p>This app opens your company's Skanyxx. It does not know the address yet.</p>
        <p>Ask your Skanyxx owner for it, then set <code>"RemoteUrl"</code> under <code>"Skanyxx"</code> in
        <code>{{WebUtility.HtmlEncode(configPath)}}</code>, for example <code>"RemoteUrl": "https://skanyxx.example.com"</code>,
        and restart the app.</p>
        </body></html>
        """;
}
