using System.Text;
using MediatR;

namespace Skanyxx.Core.Platform.Identity;

/// <summary>
/// Replaces the Microsoft sign-in settings and the whole group map; applies on the next sign-in, no restart.
/// <paramref name="ClientSecret"/> null or empty keeps the stored one. Printed as <c>***</c>: it is never logged.
/// </summary>
public sealed record SaveEntraSettingsCommand(
    string ActorId, bool Enabled, string TenantId, string ClientId, string? ClientSecret, IReadOnlyList<EntraGroupMapDto> Groups)
    : IRequest<Outcome<EntraSettingsDto>>
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"ActorId = {ActorId}, Enabled = {Enabled}, TenantId = {TenantId}, ClientId = {ClientId}, " +
            $"ClientSecret = {(string.IsNullOrEmpty(ClientSecret) ? "unchanged" : "***")}, Groups = {Groups.Count}");
        return true;
    }
}
