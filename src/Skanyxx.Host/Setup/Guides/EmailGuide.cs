namespace Skanyxx.Host.Setup.Guides;

/// <summary>Outgoing email for invites and password resets. Facts: README "Identity and security model" → "Email", "Password reset".</summary>
public static class EmailGuide
{
    public static SetupGuide Guide { get; } = new(
        Id: "email",
        Title: "Email and password reset",
        Readme: "Identity and security model → Email (D150), Password reset (D156, D157)",
        What: "With SMTP configured Skanyxx emails invite links and password-reset links, and /Login offers \"Forgot your password?\". " +
              "Without it the owner gets each link once and sends it by hand, and self-service reset is off.",
        Prerequisites:
        [
            "An SMTP server or relay (Office 365 / Gmail: STARTTLS on 587 with an app password or a relay connector).",
            "Identity:PublicBaseUrl set, since links are built on it.",
        ],
        Settings:
        [
            new("Identity:Smtp:Host", "No email is sent: invite and reset links are shown to the owner instead, and \"Forgot your password?\" is hidden.",
                "Your SMTP server's host name."),
            new("Identity:Smtp:From", "Mail has no sender address, so it cannot be sent.", "The sender address, e.g. skanyxx@acme.com."),
            new("Identity:PublicBaseUrl", "Emailed links have no address to point at.", "Where people reach Skanyxx (Helm: skanyxx.publicUrl)."),
            new("Identity:Smtp:UserName", "Mail is sent without authentication (fine for a relay that allows it).",
                "The SMTP account. Its password goes in Identity__Smtp__Password from a secret store, never a committed file.", Optional: true),
        ],
        Local:
        [
            new("Run a local catcher such as mailpit on loopback (Security None is allowed only for a loopback host)."),
            new("Start the Host with SMTP settings.",
                "Identity__Smtp__Host=127.0.0.1 Identity__Smtp__Port=1025 Identity__Smtp__Security=None Identity__Smtp__From=skanyxx@localhost scripts/dev/first-hour.sh host"),
        ],
        Helm:
        [
            new("Put the non-secret settings in skanyxx.extraEnv: Identity__Smtp__Host, Identity__Smtp__Port (587), Identity__Smtp__From, Identity__Smtp__UserName."),
            new("Put the password in a Secret with the key Identity__Smtp__Password and reference it from skanyxx.extraEnvFrom.",
                "read -rs P && printf %s \"$P\" | kubectl -n <namespace> create secret generic skanyxx-smtp --from-file=Identity__Smtp__Password=/dev/stdin; unset P"),
            new("A relay on a private network needs a rule in networkPolicy.skanyxxEgress.extra (ports 443, 465 and 587 to the internet are open already). Apply.",
                "helm upgrade skanyxx deploy/helm/skanyxx -n <namespace> --reuse-values -f <your-values.yaml>"),
            new("Drop the query string from the ingress access log for /Invite and /ResetPassword, or treat those logs as secret: the links carry their token there."),
        ],
        Verify:
        [
            new("Invite someone on People: the answer says the link was emailed, and the mail arrives."),
            new("/Login shows \"Forgot your password?\"."),
        ],
        Errors:
        [
            new("Startup stops with an SMTP reason", "Fix the named setting (e.g. Security None with a non-loopback host, or UserName without Password)."),
            new("The owner sees the link instead of \"emailed\"", "The send failed: the Host logs one Warning with server, port and error type."),
        ]);
}
