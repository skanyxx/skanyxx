using System.Net;
using System.Text.Json;
using Skanyxx.Module.Sandboxes.Features;
using Grpc.Core;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Tests.Infrastructure;

namespace Skanyxx.Module.Sandboxes.Tests;

public sealed class WatchTests : SandboxesTestBase
{
    private async Task<(HttpResponseMessage Response, List<(string Event, SandboxTaskEvent Data)> Frames)> WatchAsync(string name, SandboxesApp? app = null)
    {
        var response = await (app ?? App).Client().GetAsync($"/api/sandboxes/tasks/{name}/watch");
        var body = await response.Content.ReadAsStringAsync();
        var frames = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(block => !block.StartsWith(':'))
            .Select(block => block.Split('\n'))
            .Select(lines => (lines[0]["event: ".Length..],
                JsonSerializer.Deserialize<SandboxTaskEvent>(lines[1]["data: ".Length..], SandboxesApp.Json)!))
            .ToList();
        return (response, frames);
    }

    [Fact]
    public async Task Watch_RelaysAxFrames_ThenAFinalRead()
    {
        Ax.Seed("fix-42", "ana", phase: "Pending");

        var (response, frames) = await WatchAsync("fix-42");

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(["initial", "modified", "final"], frames.Select(f => f.Event));
        Assert.Equal(["Pending", "Running", "Running"], frames.Select(f => f.Data.Task!.Phase));
        Assert.All(frames, f => Assert.Equal(f.Event, f.Data.Kind));
    }

    [Fact]
    public async Task Watch_EndsWithGone_WhenTheTaskWasDeletedMeanwhile()
    {
        Ax.Seed("fix-42", "ana", phase: "Pending");
        Ax.WatchHangs = true;

        var watch = WatchAsync("fix-42");
        await Ax.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Ax.Remove("fix-42");
        var (_, frames) = await watch;

        Assert.Equal(["initial", "gone"], frames.Select(f => f.Event));
        Assert.Null(frames[^1].Data.Task);
    }

    [Fact]
    public async Task Watch_WindowEnds_AHangingStream_WithAFinalRead()
    {
        await using var app = await StartAppAsync(s => s["Sandboxes:WatchSeconds"] = "1");
        Ax.Seed("fix-42", "ana", phase: "Pending");
        Ax.WatchHangs = true;

        var started = DateTime.UtcNow;
        var (_, frames) = await WatchAsync("fix-42", app);

        Assert.Equal(["initial", "final"], frames.Select(f => f.Event));
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(StatusCode.Unavailable, "AX unreachable.")]
    [InlineData(StatusCode.Internal, "AX request failed.")]
    public async Task Watch_UpstreamFailureMidStream_IsATerminalErrorFrame_WithoutAxText(StatusCode status, string message)
    {
        Ax.Seed("fix-42", "ana", phase: "Pending");
        Ax.WatchFailsWith = status;

        var response = await App.Client().GetAsync("/api/sandboxes/tasks/fix-42/watch");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("event: initial", body);
        Assert.Contains($"event: error\ndata: {{\"kind\":\"error\",\"task\":null,\"error\":\"{message}\"}}", body);
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("10.1.2.3", body);
    }

    private const string Url = "/api/sandboxes/tasks/fix-42/watch";

    // CR test gap: a closed browser tab must end the AX stream now, not when the window runs out, and log nothing.
    [Fact]
    public async Task ClientDisconnect_TearsDownTheAxStream_WithoutAWarning()
    {
        await using var app = await StartAppAsync(s => s["Sandboxes:WatchSeconds"] = "60");
        Ax.Seed("fix-42", "ana", phase: "Pending");
        Ax.WatchHangs = true;

        using (var client = app.Client())
        {
            using var response = await client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);
            await using var stream = await response.Content.ReadAsStreamAsync();
            Assert.True(await stream.ReadAsync(new byte[4096]) > 0);
        }

        await Ax.WatchCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await app.Logs.WaitForRequestFinishedAsync(Url);
        Assert.Empty(app.Logs.Warnings);
    }

    // SEC L2 / CR M5: watches are capped per user and in total, and a closed one frees its slot.
    [Fact]
    public async Task OpenWatches_AreCappedPerUserAndInTotal()
    {
        await using var app = await StartAppAsync(s =>
        {
            s["Sandboxes:WatchSeconds"] = "60";
            s["Sandboxes:MaxWatchesPerUser"] = "2";
            s["Sandboxes:MaxWatches"] = "3";
        });
        Ax.Seed("fix-42", "ana", phase: "Pending");
        Ax.WatchHangs = true;
        using var ana = app.Client();
        using var dan = app.Client(SandboxesApp.Other);
        using var eve = app.Client("eve");
        Task<HttpResponseMessage> Open(HttpClient client) => client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);

        using var first = await Open(ana);
        using var second = await Open(ana);
        using var third = await Open(ana);
        using var dans = await Open(dan);
        using var eves = await Open(eve);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests, HttpStatusCode.OK, HttpStatusCode.TooManyRequests],
            new[] { first, second, third, dans, eves }.Select(r => r.StatusCode));

        first.Dispose();
        var reopened = HttpStatusCode.TooManyRequests;
        for (var i = 0; i < 100 && reopened != HttpStatusCode.OK; i++)
        {
            using var again = await Open(ana);
            reopened = again.StatusCode;
            if (reopened != HttpStatusCode.OK)
                await Task.Delay(50);
        }
        Assert.Equal(HttpStatusCode.OK, reopened);
    }

    // CR2 m2: a caller already at its cap is refused before Skanyxx asks AX anything.
    [Fact]
    public async Task AWatchOverTheCap_CostsNoAxCall()
    {
        await using var app = await StartAppAsync(s =>
        {
            s["Sandboxes:WatchSeconds"] = "60";
            s["Sandboxes:MaxWatchesPerUser"] = "1";
        });
        Ax.Seed("fix-42", "ana", phase: "Pending");
        Ax.WatchHangs = true;
        using var ana = app.Client();

        using var open = await ana.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);
        await Ax.WatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var callsBefore = Ax.Atespaces.Count;
        using var refused = await ana.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(callsBefore, Ax.Atespaces.Count);
    }

    // CR2 test gap: a stream that dies of something other than gRPC still frees its slot.
    [Fact]
    public async Task AWatchThatThrowsMidStream_FreesItsSlot()
    {
        await using var app = await StartAppAsync(s => s["Sandboxes:MaxWatchesPerUser"] = "1");
        Ax.Seed("fix-42", "ana", phase: "Pending");
        Ax.ModifiedIsCorrupt = true;
        using var ana = app.Client();

        try
        {
            using var broken = await ana.GetAsync(Url);
            await broken.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException)
        {
            // The response was cut after the first frame; the slot is what matters.
        }
        Ax.ModifiedIsCorrupt = false;
        Ax.Seed("fix-42", "ana", phase: "Pending");

        var reopened = HttpStatusCode.TooManyRequests;
        for (var i = 0; i < 100 && reopened != HttpStatusCode.OK; i++)
        {
            using var again = await ana.GetAsync(Url);
            reopened = again.StatusCode;
            if (reopened != HttpStatusCode.OK)
                await Task.Delay(50);
        }
        Assert.Equal(HttpStatusCode.OK, reopened);
    }

    [Fact]
    public async Task AQuietWatch_SendsKeepAliveComments_AndAsksProxiesNotToBuffer()
    {
        await using var app = await StartAppAsync(s =>
        {
            s["Sandboxes:WatchSeconds"] = "3";
            s["Sandboxes:KeepAliveSeconds"] = "1";
        });
        Ax.Seed("fix-42", "ana", phase: "Pending");
        Ax.WatchHangs = true;

        var response = await app.Client().GetAsync(Url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("no", Assert.Single(response.Headers.GetValues("X-Accel-Buffering")));
        Assert.Contains("\n\n: keepalive\n\n", body);
        Assert.StartsWith("event: initial\n", body);
        Assert.Contains("event: final\n", body);
    }

    // SEC L4 / CR N2: event names come from a fixed set, never from AX's action text.
    [Fact]
    public async Task EventNames_NeverCarryAxText()
    {
        Ax.Seed("fix-42", "ana", phase: "Pending");
        Ax.ModifiedAction = "PWNED\ndata: {\"kind\":\"pwned\"}\n\nevent: pwned";

        var response = await App.Client().GetAsync(Url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("pwned", body, StringComparison.OrdinalIgnoreCase);
        var events = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(b => b.Split('\n')[0]).ToList();
        Assert.Equal(["event: initial", "event: modified", "event: final"], events);
    }
}
