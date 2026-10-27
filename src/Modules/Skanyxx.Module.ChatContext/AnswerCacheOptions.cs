using System.ComponentModel.DataAnnotations;

namespace Skanyxx.Module.ChatContext;

/// <summary>D241: repeated first questions answered from Dragonfly/Redis. Off unless enabled.</summary>
public sealed class AnswerCacheOptions
{
    public bool Enabled { get; set; }

    /// <summary>A StackExchange.Redis configuration string (<c>dragonfly:6379,password=…</c>); required when enabled.</summary>
    public string Redis { get; set; } = "";

    [Range(1, 1_440)]
    public int TtlMinutes { get; set; } = 30;
}
