using Skanyxx.Module.Memory.Domain;

namespace Skanyxx.Module.Memory.Tests;

/// <summary>D076: the token's signature, claims and expiry, without a database.</summary>
public sealed class SandboxTokenCodecTests
{
    private const string Key = "a-sandbox-token-key-of-at-least-32-characters";
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly SandboxTokenCodec.Claims Claims = new("fix-42", "ax-fix-42-0a1b2c3d", "0a0a0a0a-0000-4000-8000-000000000001", Now.AddHours(1));

    [Fact]
    public void ASignedToken_ReadsBackItsClaims()
    {
        var token = SandboxTokenCodec.Sign(Claims, Key);

        var read = SandboxTokenCodec.Read(token, Key, Now, out _);

        Assert.StartsWith("skx_sbx_", token);
        Assert.Equal(Claims, read);
        Assert.NotEqual(token, SandboxTokenCodec.Sign(Claims, Key));
    }

    [Fact]
    public void AnotherKey_FailsTheSignature()
    {
        Assert.Null(SandboxTokenCodec.Read(SandboxTokenCodec.Sign(Claims, Key), Key + "x", Now, out var failure));
        Assert.Contains("signature", failure);
    }

    [Fact]
    public void AnExpiredToken_IsRefused()
    {
        var token = SandboxTokenCodec.Sign(Claims, Key);

        Assert.Null(SandboxTokenCodec.Read(token, Key, Claims.ExpiresAt, out var failure));
        Assert.Contains("expired", failure);
        Assert.NotNull(SandboxTokenCodec.Read(token, Key, Claims.ExpiresAt.AddSeconds(-1), out _));
    }

    [Fact]
    public void ChangedClaims_FailTheSignature()
    {
        var token = SandboxTokenCodec.Sign(Claims, Key);
        var other = SandboxTokenCodec.Sign(Claims with { AgentId = "ax-other-0e0f0a0b" }, Key);
        // Another sandbox's claims with this token's signature.
        var spliced = other[..other.LastIndexOf('.')] + token[token.LastIndexOf('.')..];

        Assert.Null(SandboxTokenCodec.Read(spliced, Key, Now, out var failure));
        Assert.Contains("signature", failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("skx_mem_abcdefghijklmnopqrstuvwxyzabcdefghijklmnopq")]
    [InlineData("skx_sbx_")]
    [InlineData("skx_sbx_.")]
    [InlineData("skx_sbx_!!!.!!!")]
    [InlineData("skx_sbx_e30.AAAA")]
    public void NotAToken_IsRefused(string token) =>
        Assert.Null(SandboxTokenCodec.Read(token, Key, Now, out _));

    [Fact]
    public void AnOverlongToken_IsRefusedBeforeAnyWork() =>
        Assert.Null(SandboxTokenCodec.Read("skx_sbx_" + new string('a', 2000) + ".x", Key, Now, out _));
}
