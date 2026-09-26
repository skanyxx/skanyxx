namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
