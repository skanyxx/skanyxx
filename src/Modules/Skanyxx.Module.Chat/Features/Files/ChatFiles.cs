using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Files;

/// <summary>
/// Chat files on disk, one folder per person (named by a hash of their id): <c>{id}.bin</c> and its record
/// <c>{id}.json</c>. A file is found only in the caller's own folder, so another person's file id is simply unknown.
/// Ids are 32 lowercase hex characters, never a path. One person's writes run one at a time (<see cref="OwnerLocks"/>),
/// so the cap check and the write are one step; a record is written beside itself and renamed into place, so a reader
/// never sees half of one; a record that does not parse (torn by a crash, edited by hand) is skipped and logged, never thrown (D326).
/// </summary>
internal sealed class ChatFiles(IOptions<ChatOptions> options, IHostEnvironment env, TimeProvider clock, ILogger<ChatFiles> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ChatFileOptions _options = options.Value.Files;
    private readonly string _root = Root(options.Value.Files, env);
    private readonly OwnerLocks _locks = new();

    public static string Root(ChatFileOptions options, IHostEnvironment env) => Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Path));

    public static bool IsUnder(string path, string folder) =>
        WithSeparator(path).StartsWith(WithSeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase);

    private static string WithSeparator(string path) => Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;

    public static bool IsId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigitLower);

    public ChatFileOptions Options => _options;

    /// <summary><see cref="ChatFileKinds"/> for an allowed extension, else null.</summary>
    public string? KindOf(string extension) =>
        _options.TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? ChatFileKinds.Text
        : _options.ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? ChatFileKinds.Image
        : null;

    /// <summary>The size limit for a file named <paramref name="name"/>; 0 when Chat does not take its type.</summary>
    public int MaxBytesOf(string name) => KindOf(Path.GetExtension(name)) switch
    {
        ChatFileKinds.Text => _options.MaxTextBytes,
        ChatFileKinds.Image => _options.MaxImageBytes,
        _ => 0
    };

    /// <summary>
    /// Stores a file the upload validator accepted; text is scanned for secrets. Files never sent go first; null when the
    /// person already keeps <see cref="ChatFileOptions.MaxFilesPerPerson"/> files.
    /// </summary>
    public Task<StoredChatFile?> SaveAsync(string owner, string name, byte[] bytes, CancellationToken ct) =>
        _locks.RunAsync(owner, async () =>
        {
            SweepLeftovers(owner);
            var kept = 0;
            foreach (var record in await RecordsAsync(owner, ct))
                if (record.ConversationId is null && record.CreatedAt < clock.GetUtcNow().AddHours(-_options.UnsentHours))
                    DeleteFiles(owner, record.Id);
                else
                    kept++;
            if (kept >= _options.MaxFilesPerPerson)
                return null;

            var extension = Path.GetExtension(name).ToLowerInvariant();
            var kind = KindOf(extension)!;
            var text = kind == ChatFileKinds.Text ? FileContent.Text(bytes)! : null;
            var file = new StoredChatFile(
                Guid.NewGuid().ToString("N"), name, kind,
                text is null ? FileContent.ImageType(extension, bytes)! : "text/plain; charset=utf-8",
                bytes.Length, text is not null && SecretText.Redact(text) != text, null, clock.GetUtcNow());
            Directory.CreateDirectory(Folder(owner));
            // Past the cap check the write is bounded by the kind's limit and finishes even if the caller leaves; a
            // failure takes its own pieces with it, so nothing outside the cap stays behind.
            try
            {
                await File.WriteAllBytesAsync(BytesPath(owner, file.Id), bytes, CancellationToken.None);
                await WriteRecordAsync(owner, file, CancellationToken.None);
            }
            catch
            {
                File.Delete(BytesPath(owner, file.Id));
                File.Delete(TempPath(owner, file.Id));
                throw;
            }
            return (StoredChatFile?)file;
        }, ct);

    /// <summary>The caller's file by id; null when there is none or its record does not parse.</summary>
    public async Task<StoredChatFile?> FindAsync(string owner, string id, CancellationToken ct)
    {
        if (!IsId(id) || !File.Exists(BytesPath(owner, id)))
            return null;
        var path = RecordPath(owner, id);
        try
        {
            return Parse(path, await File.ReadAllBytesAsync(path, ct));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public Task<byte[]> ReadAsync(string owner, StoredChatFile file, CancellationToken ct) => File.ReadAllBytesAsync(BytesPath(owner, file.Id), ct);

    /// <summary>The file's bytes, open; null when they are gone (a delete since the file was found).</summary>
    public Stream? OpenRead(string owner, StoredChatFile file)
    {
        try
        {
            return File.OpenRead(BytesPath(owner, file.Id));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public Task AttachAsync(string owner, StoredChatFile file, string conversationId, CancellationToken ct) =>
        file.ConversationId == conversationId
            ? Task.CompletedTask
            : _locks.RunAsync(owner, async () =>
            {
                // Deleted since it was found: attaching must not bring its record back without its bytes.
                if (File.Exists(RecordPath(owner, file.Id)))
                    await WriteRecordAsync(owner, file with { ConversationId = conversationId }, ct);
            }, ct);

    public Task DeleteAsync(string owner, StoredChatFile file, CancellationToken ct) =>
        _locks.RunAsync(owner, () =>
        {
            DeleteFiles(owner, file.Id);
            return Task.CompletedTask;
        }, ct);

    /// <summary>Every file sent in the conversation goes with it.</summary>
    public Task DeleteConversationAsync(string owner, string conversationId, CancellationToken ct) =>
        _locks.RunAsync(owner, async () =>
        {
            foreach (var file in await RecordsAsync(owner, ct))
                if (file.ConversationId == conversationId)
                    DeleteFiles(owner, file.Id);
        }, ct);

    /// <summary>A first turn that left no conversation gives its files back: unsent again, ready for a retry.</summary>
    public Task DetachConversationAsync(string owner, string conversationId, CancellationToken ct) =>
        _locks.RunAsync(owner, async () =>
        {
            foreach (var file in await RecordsAsync(owner, ct))
                if (file.ConversationId == conversationId)
                    await WriteRecordAsync(owner, file with { ConversationId = null }, ct);
        }, ct);

    // Only under the owner's lock, so no record changes while this reads.
    private async Task<List<StoredChatFile>> RecordsAsync(string owner, CancellationToken ct)
    {
        var records = new List<StoredChatFile>();
        if (Directory.Exists(Folder(owner)))
            foreach (var path in Directory.EnumerateFiles(Folder(owner), "*.json"))
                if (Parse(path, await File.ReadAllBytesAsync(path, ct)) is { } record)
                    records.Add(record);
        return records;
    }

    private StoredChatFile? Parse(string path, byte[] json)
    {
        try
        {
            if (JsonSerializer.Deserialize<StoredChatFile>(json, Json) is { } record && IsId(record.Id))
                return record;
            logger.LogWarning("Chat file record {Path} holds no file; it is skipped", path);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Chat file record {Path} does not parse; it is skipped", path);
        }
        return null;
    }

    /// <summary>
    /// Under the owner's lock no write is in flight, so a <c>.tmp</c>, or a <c>.bin</c> without its record, is what a
    /// crash or a pod kill left; neither is counted against the cap, so it goes.
    /// </summary>
    private void SweepLeftovers(string owner)
    {
        if (!Directory.Exists(Folder(owner)))
            return;
        foreach (var path in Directory.EnumerateFiles(Folder(owner), "*.tmp"))
            File.Delete(path);
        foreach (var path in Directory.EnumerateFiles(Folder(owner), "*.bin"))
            if (!File.Exists(Path.ChangeExtension(path, ".json")))
                File.Delete(path);
    }

    private async Task WriteRecordAsync(string owner, StoredChatFile file, CancellationToken ct)
    {
        var temporary = TempPath(owner, file.Id);
        await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(file, Json), ct);
        File.Move(temporary, RecordPath(owner, file.Id), overwrite: true);
    }

    private void DeleteFiles(string owner, string id)
    {
        File.Delete(BytesPath(owner, id));
        File.Delete(RecordPath(owner, id));
    }

    private string Folder(string owner) => Path.Combine(_root, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(owner)))[..32]);

    private string BytesPath(string owner, string id) => Path.Combine(Folder(owner), id + ".bin");

    private string RecordPath(string owner, string id) => Path.Combine(Folder(owner), id + ".json");

    private string TempPath(string owner, string id) => Path.Combine(Folder(owner), id + ".tmp");
}
