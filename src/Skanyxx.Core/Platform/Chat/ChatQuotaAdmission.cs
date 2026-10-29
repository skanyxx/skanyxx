namespace Skanyxx.Core.Platform.Chat;

/// <summary>
/// <see cref="IChatQuota.AdmitAsync"/>'s answer: a <see cref="Refusal"/>, or an admitted turn whose in-flight place is
/// given back on <see cref="Dispose"/> (once, however often it is called).
/// </summary>
public sealed class ChatQuotaAdmission(string? refusal, Action? release = null) : IDisposable
{
    private Action? _release = release;

    public static ChatQuotaAdmission Admitted() => new(null);

    public static ChatQuotaAdmission Refused(string why) => new(why);

    public string? Refusal { get; } = refusal;

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
