using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Skanyxx.Module.Sandboxes.Tests.Infrastructure;

/// <summary><see cref="FakeAx"/> on real Kestrel, cleartext HTTP/2 only — the way ax-server listens.</summary>
public sealed class FakeAxServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeAxServer(WebApplication app, FakeAx ax) => (_app, Ax) = (app, ax);

    public FakeAx Ax { get; }
    public string Url => _app.Urls.First();

    public static async Task<FakeAxServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2));
        builder.Logging.ClearProviders();
        var ax = new FakeAx();
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(ax);
        var app = builder.Build();
        app.MapGrpcService<FakeAx>();
        await app.StartAsync();
        return new FakeAxServer(app, ax);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
