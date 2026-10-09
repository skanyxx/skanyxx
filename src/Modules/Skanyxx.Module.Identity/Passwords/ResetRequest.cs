using System.Text;

namespace Skanyxx.Module.Identity.Passwords;

/// <summary>A "forgot your password?" waiting for <see cref="ResetRequestWorker"/>: the address typed, the caller's address, the link base.</summary>
internal sealed record ResetRequest(string Email, string? RemoteIp, string LinkBase)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Email = ***, RemoteIp = {RemoteIp}, LinkBase = {LinkBase}");
        return true;
    }
}
