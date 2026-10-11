namespace Skanyxx.Module.Memory.Tests.Infrastructure;

/// <summary>Postgres and MinIO together, for the tests of bodies in the object store (D180–D184).</summary>
[CollectionDefinition(Name)]
public sealed class ObjectStoreCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<MinioFixture>
{
    public const string Name = "object-store";
}
