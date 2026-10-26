using System.Security.Cryptography;
using Skanyxx.Module.Agents.Plugins.Sources;
using static Skanyxx.Host.Tests.Infrastructure.PluginFixtures;

namespace Skanyxx.Host.Tests;

/// <summary>An uploaded or downloaded zip is untrusted (P9): paths stay inside, links are refused, and size is bounded by what decompresses.</summary>
public sealed class ZipPluginSourceTests
{
    [Fact]
    public void AGitHubStyleArchive_LosesItsTopFolder_DigestIsTheZipsSha256()
    {
        var zip = Zip(("ai-plugin-e0fbf5e/", []), ("ai-plugin-e0fbf5e/.mcp.json", Text("{}")), ("ai-plugin-e0fbf5e/agents/a.md", Text("A")),
            ("__MACOSX/ai-plugin-e0fbf5e/._a.md", Text("junk")));

        var archive = ZipPluginSource.Read(zip);

        Assert.Equal([".mcp.json", "agents/a.md"], archive.Tree.Paths.Order(StringComparer.Ordinal));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(zip)), archive.Digest);
    }

    // D223: a download is read from its buffer, not from a copy of it; the result is the same as from the bytes.
    [Fact]
    public async Task AStream_ReadsLikeItsBytes()
    {
        var zip = Zip(("p/agents/a.md", Text("A")), ("p/.mcp.json", Text("{}")));

        var streamed = await ZipPluginSource.ReadAsync(new MemoryStream(zip), 1024 * 1024, TestContext.Current.CancellationToken);

        Assert.Equal(ZipPluginSource.Read(zip).Digest, streamed.Digest);
        Assert.Equal(["agents/a.md", ".mcp.json"], streamed.Tree.Paths);
        Assert.Equal("A", System.Text.Encoding.UTF8.GetString(streamed.Tree.Read("agents/a.md")!));
    }

    [Fact]
    public void APluginLayoutFolder_IsNotStripped()
    {
        var archive = ZipPluginSource.Read(Zip(("agents/a.md", Text("A")), ("agents/b.md", Text("B"))));

        Assert.Equal(["agents/a.md", "agents/b.md"], archive.Tree.Paths.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("../evil.md")]
    [InlineData("plugin/../../evil.md")]
    [InlineData("/etc/passwd")]
    [InlineData("plugin\\agents\\a.md")]
    [InlineData("C:/windows/a.md")]
    [InlineData("plugin/./a.md")]
    [InlineData("plugin//a.md")]
    public void PathsThatLeaveTheTree_RefuseTheZip(string path)
    {
        var ex = Assert.Throws<PluginSourceException>(() => ZipPluginSource.Read(Zip((path, Text("x")))));

        Assert.Contains("refused", ex.Message);
    }

    [Fact]
    public void ASymlink_RefusesTheZip()
    {
        var zip = Zip([("plugin/agents/a.md", Text("A"), null), ("plugin/agents/link.md", Text("/etc/passwd"), 0xA1FF)]);

        var ex = Assert.Throws<PluginSourceException>(() => ZipPluginSource.Read(zip));

        Assert.Contains("symbolic link", ex.Message);
    }

    [Fact]
    public void TooManyEntries_RefusesTheZip()
    {
        var zip = Zip([.. Enumerable.Range(0, 2_001).Select(i => ($"p/{i}.md", Text("x")))]);

        Assert.Contains("at most 2000", Assert.Throws<PluginSourceException>(() => ZipPluginSource.Read(zip)).Message);
    }

    [Fact]
    public void ExpandingPast50MB_RefusesTheZip()
    {
        var megabyte = new byte[1024 * 1024];
        var zip = Zip([.. Enumerable.Range(0, 51).Select(i => ($"p/{i}.bin", megabyte))]);
        Assert.True(zip.Length < 1024 * 1024, "a bomb-ish zip: small packed, large unpacked");

        Assert.Contains("more than 50 MB", Assert.Throws<PluginSourceException>(() => ZipPluginSource.Read(zip)).Message);
    }

    [Fact]
    public void FilesOver1MB_AreSkipped()
    {
        var archive = ZipPluginSource.Read(Zip(("p/agents/a.md", Text("A")), ("p/assets/video.bin", new byte[1024 * 1024 + 1])));

        Assert.Equal(["agents/a.md"], archive.Tree.Paths);
    }

    [Fact]
    public void ADeclaredSizeThatLies_NeverExpandsPastIt()
    {
        var zip = Zip(("p/a.md", new byte[2 * 1024 * 1024]));
        // Claim 100 bytes in both the local header and the central directory; the data still inflates to 2 MB.
        foreach (var (signature, offset) in new[] { (0x04034b50, 22), (0x02014b50, 24) })
        {
            var at = IndexOf(zip, BitConverter.GetBytes(signature));
            BitConverter.GetBytes(100).CopyTo(zip, at + offset);
        }

        var file = ZipPluginSource.Read(zip).Tree.Read("a.md");

        Assert.Equal(100, file!.Length);
    }

    [Fact]
    public void ADuplicatePath_RefusesTheZip() =>
        Assert.Contains("twice", Assert.Throws<PluginSourceException>(() => ZipPluginSource.Read(Zip(("p/a.md", Text("1")), ("p/a.md", Text("2"))))).Message);

    [Fact]
    public void NotAZip_IsRefused() =>
        Assert.Equal("Not a zip file.", Assert.Throws<PluginSourceException>(() => ZipPluginSource.Read(Text("hello"))).Message);

    [Fact]
    public async Task AStreamLargerThanAllowed_IsRefusedWhileReading()
    {
        using var stream = new MemoryStream(new byte[2048]);

        var ex = await Assert.ThrowsAsync<PluginSourceException>(() => ZipPluginSource.ReadAsync(stream, 1024, TestContext.Current.CancellationToken));

        Assert.Contains("larger than", ex.Message);
    }

    private static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);
}
