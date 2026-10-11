using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Bodies;

/// <summary>
/// Card bodies (D014, D180–D183): a body over <see cref="ObjectStoreOptions.ThresholdBytes"/> goes to the bucket under the
/// SHA-256 of its bytes, Postgres keeps its first <see cref="CardLimits.BodyExcerpt"/> characters and <c>source</c> its
/// pointer. With no store configured every body stays in Postgres, as before.
/// </summary>
internal sealed class CardBodies : IDisposable
{
    private const string ContentType = "text/plain; charset=utf-8";

    /// <summary>Strict, so a lone surrogate throws instead of being stored as U+FFFD (the validator refuses it first).</summary>
    internal static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ObjectStoreOptions _options;
    private readonly AmazonS3Client? _s3;
    private readonly ILogger<CardBodies> _logger;

    public CardBodies(IOptions<MemoryOptions> options, ILogger<CardBodies> logger)
    {
        _options = options.Value.ObjectStore;
        _logger = logger;
        if (!_options.Enabled)
            return;
        _s3 = new AmazonS3Client(new BasicAWSCredentials(_options.AccessKey, _options.SecretKey), new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            AuthenticationRegion = _options.Region,
            ForcePathStyle = true,
            // A black-holed store fails a write or /body in about a minute at most, not the SDK's 100 s per try.
            Timeout = TimeSpan.FromSeconds(15),
            MaxErrorRetry = 2,
            // Checksums only where S3 requires them: S3-compatible servers (MinIO forks, Garage, ...) differ on the rest.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        });
    }

    public bool Enabled => _s3 is not null;

    /// <summary>
    /// The body and source a write leaves on the row. <paramref name="current"/> is the row being updated (null: a new
    /// card); a null <paramref name="body"/> or <paramref name="source"/> keeps the current one, as before. A body sent back
    /// exactly as the excerpt it was read as, or a source equal to the current pointer, is "unchanged": a client that
    /// writes back what it read never replaces a stored body with its excerpt. An EDITED excerpt sent with the pointer is
    /// refused rather than taken for the whole body; <c>source: ""</c> replaces a stored body with a short one on purpose.
    /// </summary>
    public async Task<(string? Body, string? Source)> ResolveAsync(Card? current, string? body, string? source, CancellationToken ct)
    {
        var stored = BodyPointer.IsPointer(current?.Source);
        if (BodyPointer.IsPointer(source) && source != current?.Source)
            throw Refusal("source", "A source of this shape (s3://…/cards/sha256/…) is set by memory itself; use another link.");
        var echoed = stored && source == current!.Source;
        if (echoed)
            source = null;

        if (stored && (body is null || body == current!.Body))
            return source is null
                ? (current!.Body, current.Source)
                : throw Refusal("source", "This card's body is in the object store and source is its pointer: send the full body to replace both, or leave source out.");
        if (body is null)
            return (current?.Body, source ?? current?.Source);

        if (echoed && !IsLarge(body))
            throw Refusal("body", "This card's body is in the object store and Body is only its excerpt: GET …/body for the full text " +
                                  "and send it edited, or send source \"\" (empty) to replace it with this short body on purpose.");
        if (!IsLarge(body))
            return (body, source ?? (stored ? null : current?.Source));
        // The pointer takes source, so a link the card would otherwise keep is never dropped silently.
        var kept = source ?? current?.Source;
        if (!string.IsNullOrEmpty(kept) && !BodyPointer.IsPointer(kept))
            throw Refusal("source", $"A body over {_options.ThresholdBytes} bytes is kept in the object store and source becomes its pointer, " +
                                    $"so the card cannot keep source '{kept}': put the link in the body and send source \"\" (empty) to drop it.");
        return (Excerpt(body), await StoreAsync(body, ct));
    }

    /// <summary>The full body of a card whose source is a pointer into this store; null when the object is gone.</summary>
    public async Task<CardBody?> OpenAsync(string pointer, CancellationToken ct)
    {
        // Only the configured bucket is ever read: a pointer names a body memory wrote, never an arbitrary object.
        if (_s3 is null || !BodyPointer.TryParse(pointer, out var bucket, out var key) || bucket != _options.Bucket)
            return null;
        try
        {
            var response = await _s3.GetObjectAsync(_options.Bucket, key, ct);
            return new CardBody(response.ResponseStream, response.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (IsStoreFailure(ex, ct))
        {
            throw Unavailable(ex);
        }
    }

    /// <summary>The first characters of a body, never splitting a surrogate pair.</summary>
    public static string Excerpt(string body)
    {
        if (body.Length <= CardLimits.BodyExcerpt)
            return body;
        var length = char.IsHighSurrogate(body[CardLimits.BodyExcerpt - 1]) ? CardLimits.BodyExcerpt - 1 : CardLimits.BodyExcerpt;
        return body[..length];
    }

    private bool IsLarge(string body) => _s3 is not null && Utf8.GetByteCount(body) > _options.ThresholdBytes;

    private async Task<string> StoreAsync(string body, CancellationToken ct)
    {
        var bytes = Utf8.GetBytes(body);
        var key = BodyPointer.KeyPrefix + Convert.ToHexStringLower(SHA256.HashData(bytes));
        try
        {
            try
            {
                await PutAsync(key, bytes, ct);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchBucket")
            {
                // D184: the first stored body creates the bucket (the chart's MinIO starts empty).
                await CreateBucketAsync(ct);
                await PutAsync(key, bytes, ct);
            }
        }
        catch (Exception ex) when (IsStoreFailure(ex, ct))
        {
            throw Unavailable(ex);
        }
        return BodyPointer.Format(_options.Bucket, key);
    }

    private Task PutAsync(string key, byte[] bytes, CancellationToken ct) =>
        _s3!.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = new MemoryStream(bytes),
            ContentType = ContentType
        }, ct);

    private async Task CreateBucketAsync(CancellationToken ct)
    {
        try
        {
            // AWS refuses a location constraint of us-east-1 and requires one for every other region.
            var request = new PutBucketRequest { BucketName = _options.Bucket };
            if (_options.Region != "us-east-1")
                request.BucketRegionName = _options.Region;
            await _s3!.PutBucketAsync(request, ct);
            _logger.LogInformation("Created the card body bucket {Bucket}", _options.Bucket);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // Another replica created it first.
        }
    }

    /// <summary>
    /// The store failed, not the request: in AWSSDK v4 service errors (AccessDenied, 5xx, SlowDown) are not
    /// <see cref="AmazonClientException"/>s, and the SDK's own timeout surfaces as a cancellation nobody asked for.
    /// </summary>
    private static bool IsStoreFailure(Exception ex, CancellationToken ct) =>
        ex is AmazonServiceException or AmazonClientException or HttpRequestException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private ObjectStoreUnavailableException Unavailable(Exception ex)
    {
        _logger.LogWarning(ex, "The object store at {Endpoint} (bucket {Bucket}) failed", _options.Endpoint, _options.Bucket);
        return new ObjectStoreUnavailableException(ex);
    }

    private static ValidationException Refusal(string property, string message) => new([new ValidationFailure(property, message)]);

    public void Dispose() => _s3?.Dispose();
}
