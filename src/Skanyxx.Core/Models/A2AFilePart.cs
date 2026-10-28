namespace Skanyxx.Core.Models;

/// <summary>A file sent with a chat turn as an A2A <c>file</c> part (inline bytes), e.g. an image for a model that reads images.</summary>
public sealed record A2AFilePart(string Name, string MimeType, byte[] Bytes);
