namespace Skanyxx.Module.Identity.Audit;

/// <summary>The audit vocabulary (D152). One constant per Warning-level audit line the module logs, plus the new flows.</summary>
internal static class AuditActions
{
    public const string InviteCreated = "invite.created";
    public const string InviteRevoked = "invite.revoked";
    public const string InviteAccepted = "invite.accepted";
    public const string InviteAcceptRefused = "invite.accept_refused";
    public const string InviteLookupRefused = "invite.lookup_refused";
    public const string InviteLinkShown = "invite.link_shown";

    public const string RolesChanged = "person.roles_changed";
    public const string Disabled = "person.disabled";
    public const string Enabled = "person.enabled";
    public const string EntraLoginRemoved = "person.entra_login_removed";

    public const string BootstrapTokenSignIn = "signin.bootstrap_token";
    public const string ManagedPasswordRefused = "signin.managed_password_refused";
    public const string ManagedRefreshRefused = "refresh.managed_refused";

    public const string ResetRequested = "password.reset_requested";
    public const string ResetIssued = "password.reset_issued";
    public const string ResetCompleted = "password.reset_completed";
    public const string ResetRefused = "password.reset_refused";
    public const string ResetLinkShown = "password.reset_link_shown";

    public const string DepartmentCreated = "org.department_created";
    public const string DepartmentRenamed = "org.department_renamed";
    public const string TeamCreated = "org.team_created";
    public const string TeamRenamed = "org.team_renamed";
    public const string TeamMoved = "org.team_moved";
    public const string MemberAdded = "org.member_added";
    public const string MemberRemoved = "org.member_removed";

    public const string EntraSettingsSaved = "entra.settings_saved";
    public const string EntraAccountCreated = "entra.account_created";
    public const string EntraRemapped = "entra.account_remapped";
    public const string EntraLinked = "entra.account_linked";
    public const string EntraSignInRefused = "entra.signin_refused";
    public const string EntraLinkRefused = "entra.link_refused";
    public const string EntraCallbackRefused = "entra.callback_refused";
    public const string EntraRecheckRemapped = "entra.recheck_remapped";
    public const string EntraRecheckRefused = "entra.recheck_refused";

    public const string OwnerRouteRefused = "route.owner_refused";

    public const string RetentionPurged = "audit.retention_purged";
}
