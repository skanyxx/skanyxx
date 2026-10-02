namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Nullable because JSON can send null; the validator turns a missing value into a 400.</summary>
public sealed class CreateInviteRequest
{
    public string? Email { get; set; }
    public List<string>? Roles { get; set; }
}
