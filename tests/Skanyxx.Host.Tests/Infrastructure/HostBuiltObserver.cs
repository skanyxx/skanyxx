using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skanyxx.Host.Tests.Infrastructure;

/// <summary>
/// Captures the <see cref="IHost"/> that Program.cs builds, via the hosting "HostBuilt" diagnostic event. With
/// <c>services</c>, also adds test services after Program's own registrations, through the "HostBuilding" event
/// (what WebApplicationFactory uses): test fakes that cannot be passed as settings.
/// </summary>
public sealed class HostBuiltObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly List<IDisposable> _subscriptions = [];
    private readonly TaskCompletionSource<IHost> _built = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<IServiceCollection>? _services;

    public HostBuiltObserver(Action<IServiceCollection>? services = null)
    {
        _services = services;
        _subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
    }

    public Task<IHost> Built => _built.Task;

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "Microsoft.Extensions.Hosting")
            lock (_subscriptions)
                _subscriptions.Add(listener.Subscribe(this));
    }

    public void OnNext(KeyValuePair<string, object?> e)
    {
        if (e is { Key: "HostBuilding", Value: IHostBuilder builder } && _services is not null)
            builder.ConfigureServices((_, services) => _services(services));
        if (e is { Key: "HostBuilt", Value: IHost host })
            _built.TrySetResult(host);
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }

    public void Dispose()
    {
        lock (_subscriptions)
            _subscriptions.ForEach(s => s.Dispose());
    }
}
