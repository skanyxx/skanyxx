using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skanyxx.Module.Chat;
using Skanyxx.Module.Chat.Features.Files;

namespace Skanyxx.Host.Tests;

/// <summary>D326, D328: the file store on a folder of its own, for the races and leftovers the API cannot time.</summary>
public sealed class ChatFilesTests : IDisposable
{
    private const string Owner = "ann";

    private readonly string _root = Directory.CreateTempSubdirectory("skanyxx-chat-files-").FullName;
    private readonly ChatFiles _files;

    public ChatFilesTests() =>
        _files = new ChatFiles(Options.Create(new ChatOptions { Files = { Path = "files" } }),
            new HostingEnvironment { ContentRootPath = _root }, TimeProvider.System, NullLogger<ChatFiles>.Instance);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAttach_AfterADelete_DoesNotBringTheRecordBack()
    {
        var saved = (await _files.SaveAsync(Owner, "notes.txt", "notes"u8.ToArray(), Ct))!;
        var found = (await _files.FindAsync(Owner, saved.Id, Ct))!;

        await _files.DeleteAsync(Owner, found, Ct);
        await _files.AttachAsync(Owner, found, "conversation-1", Ct);

        Assert.Null(await _files.FindAsync(Owner, saved.Id, Ct));
        Assert.Empty(Stored("*"));
    }

    [Fact]
    public async Task ASave_SweepsWhatACrashLeft_AndKeepsEveryRecordedFile()
    {
        var kept = (await _files.SaveAsync(Owner, "kept.txt", "kept"u8.ToArray(), Ct))!;
        var folder = Path.GetDirectoryName(Assert.Single(Stored("*.bin")))!;
        await File.WriteAllTextAsync(Path.Combine(folder, new string('c', 32) + ".tmp"), "{\"id\":", Ct);
        await File.WriteAllBytesAsync(Path.Combine(folder, new string('d', 32) + ".bin"), [1, 2, 3], Ct);

        var next = (await _files.SaveAsync(Owner, "next.txt", "next"u8.ToArray(), Ct))!;

        Assert.Equal(new[] { kept.Id + ".bin", kept.Id + ".json", next.Id + ".bin", next.Id + ".json" }.Order(StringComparer.Ordinal),
            Stored("*").Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    private string[] Stored(string pattern)
    {
        var folder = Path.Combine(_root, "files");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern, SearchOption.AllDirectories) : [];
    }
}
