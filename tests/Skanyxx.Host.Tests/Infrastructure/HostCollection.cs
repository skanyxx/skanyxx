namespace Skanyxx.Host.Tests.Infrastructure;

// One collection, so tests run one at a time: they share a container (one pauses it), Program.cs captures its
// IHost through a process-wide diagnostic event, and the log test redirects the process-wide Console.
[CollectionDefinition(Name)]
public sealed class HostCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "host";
}
