namespace Skanyxx.Module.Identity.Tests.Infrastructure;

// One collection: every test resets the same database, so they run one at a time.
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
