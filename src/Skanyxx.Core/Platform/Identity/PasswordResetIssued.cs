using System.Text;

namespace Skanyxx.Core.Platform.Identity;

/// <summary><see cref="Link"/> carries the token: present only when it was not emailed (SMTP off, or sending failed).</summary>
public sealed record PasswordResetIssued(string? Link, DateTimeOffset ExpiresAt, bool Emailed)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Link = ***, ExpiresAt = {ExpiresAt:O}, Emailed = {Emailed}");
        return true;
    }
}
