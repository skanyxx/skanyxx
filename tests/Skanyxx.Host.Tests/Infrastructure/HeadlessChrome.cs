using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// Runs a test page in headless Chrome and reads one value back over the DevTools protocol (no test dependency added).
/// <see cref="Find"/> is null when this machine has no Chrome or Chromium (CHROME_PATH names one explicitly), so a test
/// can skip; a Chrome that is there but cannot start fails with what it printed, since a broken browser on a runner that
/// has one should be seen, not skipped.
/// </summary>
public static class HeadlessChrome
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string? Find()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("CHROME_PATH"),
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
            "/Applications/Chromium.app/Contents/MacOS/Chromium",
            "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable", "/usr/bin/chromium", "/usr/bin/chromium-browser",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            System.IO.Path.Combine(home, "AppData", "Local", "Google", "Chrome", "Application", "chrome.exe")
        ];
        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c));
    }

    /// <summary>
    /// Writes <paramref name="html"/> to a temporary file, opens it, and returns
    /// <c>window[<paramref name="resultName"/>]</c> once the page has set it (a promise is awaited; a string is read as JSON).
    /// </summary>
    public static async Task<JsonElement> RunAsync(string chrome, string html, string resultName)
    {
        var dir = Directory.CreateTempSubdirectory("skx-ui-");
        Process? browser = null;
        try
        {
            var page = System.IO.Path.Combine(dir.FullName, "page.html");
            await File.WriteAllTextAsync(page, html, Ct);

            var profile = System.IO.Path.Combine(dir.FullName, "chrome");
            var start = new ProcessStartInfo(chrome) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[]
                     {
                         "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--allow-file-access-from-files",
                         "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=0", $"--user-data-dir={profile}"
                     })
                start.ArgumentList.Add(arg);
            // Chrome's sandbox refuses to start as root, and CI containers often lack the kernel support it needs.
            if (OperatingSystem.IsLinux() && (Environment.UserName == "root" || Environment.GetEnvironmentVariable("CI") is not null))
                start.ArgumentList.Add("--no-sandbox");
            start.ArgumentList.Add(new Uri(page).AbsoluteUri);
            browser = Process.Start(start)!;
            var stderr = browser.StandardError.ReadToEndAsync(Ct);
            _ = browser.StandardOutput.ReadToEndAsync(Ct);

            // The page may still be loading when the socket opens: wait for its result to exist, then for it.
            var json = await EvaluateAsync(await PortAsync(profile, browser, stderr),
                $"new Promise(r => {{ const t = () => window.{resultName} ? Promise.resolve(window.{resultName}).then(v => r(typeof v === 'string' ? v : JSON.stringify(v))) : setTimeout(t, 50); t(); }})");
            return JsonDocument.Parse(json).RootElement;
        }
        finally
        {
            if (browser is { HasExited: false })
            {
                browser.Kill(entireProcessTree: true);
                await browser.WaitForExitAsync(CancellationToken.None);
            }
            browser?.Dispose();
            try { dir.Delete(recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Chrome may still hold its profile for a moment */ }
        }
    }

    /// <summary>Chrome writes the port it chose (port 0) into the profile.</summary>
    private static async Task<int> PortAsync(string profile, Process browser, Task<string> stderr)
    {
        var file = System.IO.Path.Combine(profile, "DevToolsActivePort");
        for (var waited = 0; waited < 30_000; waited += 100)
        {
            if (browser.HasExited)
                Assert.Fail($"Chrome exited ({browser.ExitCode}) before it opened its DevTools port:\n{await stderr}");
            if (File.Exists(file) && (await File.ReadAllLinesAsync(file, Ct)) is [var port, ..] && int.TryParse(port, out var number))
                return number;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("Chrome did not open its DevTools port.");
    }

    private static async Task<string> EvaluateAsync(int port, string expression)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        string? socketUrl = null;
        for (var waited = 0; socketUrl is null && waited < 30_000; waited += 100)
        {
            var targets = await http.GetFromJsonAsync<JsonElement>("/json/list", Ct);
            socketUrl = targets.EnumerateArray().Where(t => t.GetProperty("type").GetString() == "page")
                .Select(t => t.GetProperty("webSocketDebuggerUrl").GetString()).FirstOrDefault();
            if (socketUrl is null)
                await Task.Delay(100, Ct);
        }

        using var socket = new ClientWebSocket();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        await socket.ConnectAsync(new Uri(socketUrl!), timeout.Token);

        // Headless Chrome opens about:blank and then navigates to the page, so an evaluate sent in between dies with
        // its context ("Execution context was destroyed"). The expression only waits for the page's own result, so
        // it is sent again into the context that replaced it; a page that keeps navigating away still fails.
        for (var id = 1; ; id++)
        {
            var reply = await SendAsync(socket, id, expression, timeout.Token);
            if (reply.TryGetProperty("result", out var outer) && outer.GetProperty("result").TryGetProperty("value", out var value))
                return value.GetString()!;
            if (id >= 5 || !(reply.TryGetProperty("error", out var error)
                             && error.GetProperty("message").GetString() == "Execution context was destroyed."))
                throw new InvalidOperationException($"the page did not answer: {reply}");
        }
    }

    private static async Task<JsonElement> SendAsync(ClientWebSocket socket, int id, string expression, CancellationToken ct)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id, method = "Runtime.evaluate", @params = new { expression, awaitPromise = true, returnByValue = true }
        });
        await socket.SendAsync(request, WebSocketMessageType.Text, endOfMessage: true, ct);

        var buffer = new byte[64 * 1024];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer, ct);
                message.Write(buffer, 0, received.Count);
            }
            while (!received.EndOfMessage);
            var reply = JsonDocument.Parse(Encoding.UTF8.GetString(message.ToArray())).RootElement;
            if (reply.TryGetProperty("id", out var replyId) && replyId.GetInt32() == id)
                return reply;
        }
    }
}
