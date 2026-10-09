using System.Diagnostics;

namespace Skanyxx.Host.Tests;

/// <summary>
/// H4 (slice 4 QA): a Host that cannot start logs Fatal and exits 1. It used to rethrow, and an unhandled exception
/// ends in abort(), which a container's PID 1 does not act on: the pod stayed Running instead of crash-looping.
/// </summary>
[Collection(HostCollection.Name)]
public sealed class ExitCodeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AStartupFailure_IsLoggedFatal_AndExitsOne()
    {
        var (exitCode, log, _) = await RunEntryPointAsync(fixture.ConnectionString, s => s["AllowedHosts"] = "*");

        Assert.Equal(1, exitCode);
        // CR m8: the reason is logged as Fatal (and flushed) before the process exits.
        Assert.Contains(log.Split('\n'), line => line.Contains("\"@l\":\"Fatal\"") && line.Contains("AllowedHosts"));
    }

    [Fact]
    public async Task AnUnreachableDatabase_ExitsOne_Quickly()
    {
        // Nothing listens on port 1: every connection is refused at once.
        var (exitCode, log, elapsed) = await RunEntryPointAsync(
            "Host=127.0.0.1;Port=1;Database=skanyxx;Username=skanyxx;Password=x;Timeout=3", s => s["urls"] = "http://127.0.0.1:0");

        Assert.Equal(1, exitCode);
        Assert.True(elapsed < TimeSpan.FromSeconds(60), $"took {elapsed}");
        Assert.Contains(log.Split('\n'), line => line.Contains("\"@l\":\"Fatal\""));
    }

    private static async Task<(int ExitCode, string Log, TimeSpan Elapsed)> RunEntryPointAsync(
        string connectionString, Action<Dictionary<string, string>> configure)
    {
        var contentRoot = TempContentRoot.Create();
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        var watch = Stopwatch.StartNew();
        try
        {
            string[] args = HostApp.Arguments(connectionString, contentRoot, configure);
            var run = Task.Run(() => (int)typeof(Program).Assembly.EntryPoint!.Invoke(null, [args])!);
            var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(90));
            return (exitCode, console.ToString(), watch.Elapsed);
        }
        finally
        {
            Console.SetOut(original);
            TempContentRoot.Delete(contentRoot);
        }
    }
}
