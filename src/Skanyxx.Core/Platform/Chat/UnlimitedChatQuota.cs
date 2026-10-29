namespace Skanyxx.Core.Platform.Chat;

/// <summary>No token limits until a quota module replaces this (the <see cref="IChatQuota"/> hook).</summary>
public sealed class UnlimitedChatQuota : IChatQuota
{
    public Task<ChatQuotaAdmission> AdmitAsync(string userId, string agentRef, CancellationToken ct) => Task.FromResult(ChatQuotaAdmission.Admitted());

    public Task RecordAsync(ChatQuotaUsage usage, CancellationToken ct) => Task.CompletedTask;
}
