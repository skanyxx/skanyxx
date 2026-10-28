using System.Text;

namespace Skanyxx.Module.Chat.Features.Files;

/// <summary>A file's bytes must be what its extension says: UTF-8 text without NUL bytes, or an image with that format's signature.</summary>
internal static class FileContent
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string? Text(byte[] bytes)
    {
        if (Array.IndexOf(bytes, (byte)0) >= 0)
            return null;
        try
        {
            return StrictUtf8.GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>The image's media type when <paramref name="bytes"/> start with the signature of <paramref name="extension"/>'s format.</summary>
    public static string? ImageType(string extension, byte[] bytes) => extension switch
    {
        ".png" when bytes.AsSpan().StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) => "image/png",
        ".jpg" or ".jpeg" when bytes.AsSpan().StartsWith((byte[])[0xFF, 0xD8, 0xFF]) => "image/jpeg",
        ".gif" when bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8) => "image/gif",
        ".webp" when bytes.Length >= 12 && bytes.AsSpan().StartsWith("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8) => "image/webp",
        _ => null
    };
}
