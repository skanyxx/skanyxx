using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Chat.Features.Files;

/// <summary>
/// Chat files on disk, one folder per person (named by a hash of their id): <c>{id}.bin</c> and its record
/// <c>{id}.json</c>. A file is found only in the caller's own folder, so another person's file id is simply unknown.
/// Ids are 32 lowercase hex characters, never a path.
/// </summary>
internal sealed class ChatFiles(IOptions<ChatOptions> options, IHostEnvironment env, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ChatFileOptions _options = options.Value.Files;
    private readonly string _root = Root(options.Value.Files, env);

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

    /// <summary>
    /// Stores a file the upload validator accepted; text is scanned for secrets. Files never sent go first; null when the
    /// person already keeps <see cref="ChatFileOptions.MaxFilesPerPerson"/> files.
    /// </summary>
    public async Task<StoredChatFile?> SaveAsync(string owner, string name, byte[] bytes, CancellationToken ct)
    {
        var kept = 0;
        foreach (var record in await RecordsAsync(owner, ct))
            if (record.ConversationId is null && record.CreatedAt < clock.GetUtcNow().AddHours(-_options.UnsentHours))
                Delete(owner, record);
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
        await File.WriteAllBytesAsync(BytesPath(owner, file.Id), bytes, ct);
        await WriteRecordAsync(owner, file, ct);
        return file;
    }

    public async Task<StoredChatFile?> FindAsync(string owner, string id, CancellationToken ct) =>
        IsId(id) && File.Exists(RecordPath(owner, id)) && File.Exists(BytesPath(owner, id))
            ? JsonSerializer.Deserialize<StoredChatFile>(await File.ReadAllBytesAsync(RecordPath(owner, id), ct), Json)
            : null;

    public Task<byte[]> ReadAsync(string owner, StoredChatFile file, CancellationToken ct) => File.ReadAllBytesAsync(BytesPath(owner, file.Id), ct);

    public Stream OpenRead(string owner, StoredChatFile file) => File.OpenRead(BytesPath(owner, file.Id));

    public Task AttachAsync(string owner, StoredChatFile file, string conversationId, CancellationToken ct) =>
        file.ConversationId == conversationId ? Task.CompletedTask : WriteRecordAsync(owner, file with { ConversationId = conversationId }, ct);

    public void Delete(string owner, StoredChatFile file)
    {
        File.Delete(BytesPath(owner, file.Id));
        File.Delete(RecordPath(owner, file.Id));
    }

    /// <summary>Every file sent in the conversation goes with it.</summary>
    public async Task DeleteConversationAsync(string owner, string conversationId, CancellationToken ct)
    {
        foreach (var file in await RecordsAsync(owner, ct))
            if (file.ConversationId == conversationId)
                Delete(owner, file);
    }

    /// <summary>A first turn that left no conversation gives its files back: unsent again, ready for a retry.</summary>
    public async Task DetachConversationAsync(string owner, string conversationId, CancellationToken ct)
    {
        foreach (var file in await RecordsAsync(owner, ct))
            if (file.ConversationId == conversationId)
                await WriteRecordAsync(owner, file with { ConversationId = null }, ct);
    }

    private async Task<List<StoredChatFile>> RecordsAsync(string owner, CancellationToken ct)
    {
        var records = new List<StoredChatFile>();
        if (Directory.Exists(Folder(owner)))
            foreach (var path in Directory.EnumerateFiles(Folder(owner), "*.json"))
                records.Add(JsonSerializer.Deserialize<StoredChatFile>(await File.ReadAllBytesAsync(path, ct), Json)!);
        return records;
    }

    private Task WriteRecordAsync(string owner, StoredChatFile file, CancellationToken ct) =>
        File.WriteAllBytesAsync(RecordPath(owner, file.Id), JsonSerializer.SerializeToUtf8Bytes(file, Json), ct);

    private string Folder(string owner) => Path.Combine(_root, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(owner)))[..32]);

    private string BytesPath(string owner, string id) => Path.Combine(Folder(owner), id + ".bin");

    private string RecordPath(string owner, string id) => Path.Combine(Folder(owner), id + ".json");
}
