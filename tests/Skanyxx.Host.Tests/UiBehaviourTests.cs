using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Skanyxx.Host.Tests;

/// <summary>
/// ui.js's declarative hooks in a real browser: Cancel or Escape on a confirm sends nothing, a confirmed or data-once form
/// goes once (and again after a back/forward restore), and skx.ask answers only for its submit button, also after an
/// earlier save (the Audit "Save view" bug). Ui/ui-behaviour.html runs the real ui.js in headless Chrome, read over the
/// DevTools protocol (no test dependency added); skipped when this machine has no Chrome or Chromium (CHROME_PATH
/// names one explicitly).
/// </summary>
public sealed class UiBehaviourTests
{
    private static readonly string[] Checks =
    [
        "confirmOpens", "dangerFocusesCancel", "cancelSendsNothing", "escapeSendsNothing", "okSendsOnce",
        "confirmedDoesNotSendTwice", "dataOnceSendsOnce", "restoredPageCanSendAgain", "askSave", "askCancelAfterASave",
        "askEscapeAfterASave"
    ];

    [Fact]
    public async Task TheDeclarativeHooks_ActOnlyWhenThePersonMeansIt()
    {
        var chrome = Chrome();
        if (chrome is null)
            Assert.Skip("No Chrome or Chromium on this machine (set CHROME_PATH to run the ui.js behaviour checks).");

        var dir = Directory.CreateTempSubdirectory("skx-ui-");
        Process? browser = null;
        try
        {
            var uiJs = new Uri(Repo.Path("src", "Skanyxx.Host", "wwwroot", "js", "ui.js")).AbsoluteUri;
            var page = System.IO.Path.Combine(dir.FullName, "ui-behaviour.html");
            await File.WriteAllTextAsync(page, (await File.ReadAllTextAsync(Repo.Path("tests", "Skanyxx.Host.Tests", "Ui", "ui-behaviour.html"), Ct))
                .Replace("{{UI_JS}}", uiJs, StringComparison.Ordinal), Ct);

            var profile = System.IO.Path.Combine(dir.FullName, "chrome");
            var start = new ProcessStartInfo(chrome!) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[]
                     {
                         "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--allow-file-access-from-files",
                         "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=0", $"--user-data-dir={profile}", new Uri(page).AbsoluteUri
                     })
                start.ArgumentList.Add(arg);
            browser = Process.Start(start)!;
            _ = browser.StandardError.ReadToEndAsync(Ct);
            _ = browser.StandardOutput.ReadToEndAsync(Ct);

            // The page may still be loading when the socket opens: wait for its promise to exist, then for it.
            var json = await EvaluateAsync(await PortAsync(profile),
                "new Promise(r => { const t = () => window.uiBehaviour ? r(window.uiBehaviour) : setTimeout(t, 50); t(); })");
            var result = JsonDocument.Parse(json).RootElement;
            Assert.False(result.TryGetProperty("error", out var error), error.ToString());
            Assert.All(Checks, check => Assert.True(result.GetProperty(check).GetBoolean(), check));
        }
        finally
        {
            if (browser is { HasExited: false })
            {
                browser.Kill(entireProcessTree: true);
                await browser.WaitForExitAsync(CancellationToken.None);
            }
            browser?.Dispose();
            try { dir.Delete(recursive: true); } catch (IOException) { /* Chrome may still hold its profile for a moment */ }
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string? Chrome()
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

    /// <summary>Chrome writes the port it chose (port 0) into the profile.</summary>
    private static async Task<int> PortAsync(string profile)
    {
        var file = System.IO.Path.Combine(profile, "DevToolsActivePort");
        for (var waited = 0; waited < 30_000; waited += 100)
        {
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
        var request = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = 1, method = "Runtime.evaluate", @params = new { expression, awaitPromise = true, returnByValue = true }
        });
        await socket.SendAsync(request, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);

        var buffer = new byte[64 * 1024];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer, timeout.Token);
                message.Write(buffer, 0, received.Count);
            }
            while (!received.EndOfMessage);
            var reply = JsonDocument.Parse(Encoding.UTF8.GetString(message.ToArray())).RootElement;
            if (reply.TryGetProperty("id", out var id) && id.GetInt32() == 1)
                return reply.TryGetProperty("result", out var outer) && outer.GetProperty("result").TryGetProperty("value", out var value)
                    ? value.GetString()!
                    : throw new InvalidOperationException($"the page did not answer: {reply}");
        }
    }
}
