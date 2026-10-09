using System.Text;

namespace Skanyxx.Core.Platform.Email;

/// <summary>
/// One plain-text email. <see cref="TextBody"/> may carry a one-time link, so it never prints (logs, traces).
/// </summary>
public sealed record EmailMessage(string To, string Subject, string TextBody)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"To = ***, Subject = {Subject}, TextBody = ***");
        return true;
    }
}
