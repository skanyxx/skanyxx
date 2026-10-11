using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Testcontainers.Minio;

namespace Skanyxx.Module.Memory.Tests.Infrastructure;

/// <summary>
/// The chart's S3 server (D132/D140: the pgsty/minio fork, pinned by digest; multi-arch, so it runs on arm64 too), with an
/// S3 client of its own for looking into the bucket behind memory's back.
/// </summary>
public sealed class MinioFixture : IAsyncLifetime
{
    public const string Image =
        "pgsty/minio:RELEASE.2026-08-04T00-00-00Z@sha256:b6bfe7239bfc83fb90d31612d9704d86039dd714f7904b3f1ad68f211e602372";

    private readonly MinioContainer _container = new MinioBuilder(Image).Build();
    private int _buckets;

    public string Endpoint => _container.GetConnectionString();

    public IAmazonS3 S3 { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        S3 = new AmazonS3Client(new BasicAWSCredentials(_container.GetAccessKey(), _container.GetSecretKey()),
            new AmazonS3Config { ServiceURL = Endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
    }

    public async ValueTask DisposeAsync()
    {
        S3.Dispose();
        await _container.DisposeAsync();
    }

    /// <summary>A bucket name no earlier test used: each test starts from a bucket that does not exist yet.</summary>
    public string NewBucket() => $"cards-{Interlocked.Increment(ref _buckets)}";

    /// <summary>Settings that turn memory's object store on against this container.</summary>
    public Dictionary<string, string?> Settings(string bucket, int thresholdBytes = 1024, int maxBodyBytes = 1024 * 1024) => new()
    {
        ["Memory:ObjectStore:Endpoint"] = Endpoint,
        ["Memory:ObjectStore:Bucket"] = bucket,
        ["Memory:ObjectStore:AccessKey"] = _container.GetAccessKey(),
        ["Memory:ObjectStore:SecretKey"] = _container.GetSecretKey(),
        ["Memory:ObjectStore:ThresholdBytes"] = thresholdBytes.ToString(),
        ["Memory:ObjectStore:MaxBodyBytes"] = maxBodyBytes.ToString()
    };

    public async Task<List<string>> KeysAsync(string bucket)
    {
        try
        {
            return (await S3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket })).S3Objects?.Select(o => o.Key).ToList() ?? [];
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
        {
            return [];
        }
    }

    public async Task<string> ReadAsync(string bucket, string key)
    {
        using var response = await S3.GetObjectAsync(bucket, key);
        using var reader = new StreamReader(response.ResponseStream);
        return await reader.ReadToEndAsync();
    }
}
