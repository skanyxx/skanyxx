using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Skanyxx.Module.Memory;

/// <summary>
/// <c>Memory:ObjectStore</c>: an S3 bucket (MinIO in the chart) for card bodies larger than the card (D014, D180).
/// No <see cref="Endpoint"/> = off: every body stays in Postgres, capped at <see cref="Domain.CardLimits.Body"/>.
/// </summary>
public sealed partial class ObjectStoreOptions
{
    public const int MaxBodyCeiling = 8 * 1024 * 1024;

    /// <summary>The S3 endpoint, e.g. <c>http://skanyxx-minio.skanyxx.svc:9000</c> or <c>https://s3.eu-west-1.amazonaws.com</c>.</summary>
    public string? Endpoint { get; set; }

    public string Bucket { get; set; } = "skanyxx-memory";

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Signing region; MinIO accepts any, AWS needs the bucket's.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>A body of more UTF-8 bytes than this goes to the bucket; at most the Postgres column's size.</summary>
    [Range(1, Domain.CardLimits.Body)]
    public int ThresholdBytes { get; set; } = 4096;

    /// <summary>
    /// The largest body a write may carry (UTF-8 bytes) while the store is on. At most 8 MiB: the upsert's request cap is
    /// about 6× this (JSON may escape every byte as \uXXXX) and the request is buffered whole, per request.
    /// </summary>
    [Range(1, MaxBodyCeiling)]
    public int MaxBodyBytes { get; set; } = 1024 * 1024;

    public bool Enabled => !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>Configured completely, or not at all.</summary>
    internal bool IsValid() =>
        !Enabled || (Validator.TryValidateObject(this, new ValidationContext(this), null, validateAllProperties: true)
                     && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                     && BucketName().IsMatch(Bucket)
                     && !string.IsNullOrEmpty(AccessKey) && !string.IsNullOrEmpty(SecretKey)
                     && MaxBodyBytes > ThresholdBytes);

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$")]
    internal static partial Regex BucketName();
}
