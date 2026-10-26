using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Skanyxx.Host;

/// <summary>
/// <c>Skanyxx.Host open</c> / <c>url</c> (D234): what every launcher runs. It reads the same configuration as the Host,
/// opens (or prints) <c>Skanyxx:RemoteUrl</c> and exits — no listener, so no local port another account could take
/// first and serve a lookalike sign-in on.
/// </summary>
public static class OpenCommand
{
    /// <summary>The exit code, or null when the arguments are not one of these verbs (the Host starts as usual).</summary>
    public static int? Run(string[] args)
    {
        if (args is not ["open" or "url", ..])
            return null;

        try
        {
            // Same sources as the Host (appsettings.json in the content root, environment, command line); nothing is built.
            // The content root is the install folder unless one is given: run from any other folder, a planted
            // appsettings.json there must not choose where the browser goes.
            var rest = args[1..];
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = rest,
                ContentRootPath = new ConfigurationBuilder().AddCommandLine(rest).Build()[WebHostDefaults.ContentRootKey]
                    ?? AppContext.BaseDirectory
            });
            if (HostModes.Read(builder.Configuration) != HostMode.Remote)
                return Fail("This is a full Skanyxx (Server mode), not the desktop launcher: start it with the skanyxx service " +
                    "(Linux) or `Skanyxx.Host --urls http://127.0.0.1:5282` in the install folder, then open http://127.0.0.1:5282.");

            var url = RemoteClient.ReadRemoteUrl(builder.Configuration);
            if (url is null)
                return Fail($"Set \"RemoteUrl\" under \"Skanyxx\" in {Path.Combine(builder.Environment.ContentRootPath, "appsettings.json")} " +
                    "to your company's Skanyxx address (ask its owner), e.g. \"RemoteUrl\": \"https://skanyxx.example.com\".");

            if (url.IsLoopback)
                Console.Error.WriteLine($"Skanyxx: {url.Authority} is an address on this machine, shared by every account on it: " +
                    "use it for development only.");
            Console.Out.WriteLine(url.AbsoluteUri);
            if (args[0] == "open")
                Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return 0;
        }
        catch (Exception ex) // An invalid RemoteUrl or Mode, an unreadable config, no browser opener (xdg-open).
        {
            return Fail(ex.Message);
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"Skanyxx: {message}");
        // A Windows shortcut opens a console of its own that closes with the process: keep it until the message is read.
        // A console someone typed the command into has other processes in it (their shell), and is left alone.
        if (OperatingSystem.IsWindows() && !Console.IsInputRedirected && GetConsoleProcessList(new uint[2], 2) == 1)
        {
            Console.Error.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
        return 1;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList([Out] uint[] processList, uint processCount);
}
