using System.Text;

namespace Skanyxx.Core.Platform.Memory;

/// <summary>A freshly issued studio agent secret. Never printed; <see cref="Fingerprint"/> names it in kagent (D118).</summary>
public sealed record StudioSecret(string AgentId, string Secret, string Fingerprint)
{
    /// <summary>The <c>created_by</c> of every secret the studio issues; never a user id (those are GUIDs).</summary>
    public const string IssuedBy = "studio";

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"AgentId = {AgentId}, Secret = ***, Fingerprint = {Fingerprint}");
        return true;
    }
}
