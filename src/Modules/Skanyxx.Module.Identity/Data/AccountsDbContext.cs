using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// ASP.NET Core Identity's schema under <c>identity_*</c> table names, so it can share a database with other modules,
/// plus the Data Protection key ring (cookies and bearer tokens are protected with it, so every replica must share it
/// and it must survive a restart), the refresh-token chains, the owed privilege revocations, the invites, the org tree (departments ⊃ teams ⊃ members)
/// and the Microsoft Entra ID sign-in settings with their group map.
/// </summary>
public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options)
    : IdentityDbContext<IdentityUser>(options), IDataProtectionKeyContext
{
    public const string MigrationsTable = "__identity_migrations";

    /// <summary>Every hat exists as a role from the first migration; invites and role changes assign all but the owner.</summary>
    public static readonly string[] SeededRoles = [SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder, SkanyxxRoles.Employee];

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    public DbSet<Invite> Invites => Set<Invite>();

    public DbSet<PendingRevocation> PendingRevocations => Set<PendingRevocation>();

    public DbSet<OrgDepartment> Departments => Set<OrgDepartment>();

    public DbSet<OrgTeam> Teams => Set<OrgTeam>();

    public DbSet<OrgTeamMember> TeamMembers => Set<OrgTeamMember>();

    public DbSet<EntraSettings> EntraSettings => Set<EntraSettings>();

    public DbSet<EntraGroupMap> EntraGroups => Set<EntraGroupMap>();

    /// <summary>The memory scope id shape (its CHECK constraint), so every slug is a valid <c>team:</c>/<c>department:</c> scope.</summary>
    private const string SlugCheck = "~ '^[a-z0-9][a-z0-9._@-]{0,127}$'";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<IdentityUser>().ToTable("identity_users");
        // Identity checks RequireUniqueEmail in code only; the index makes it hold under concurrent writers too.
        builder.Entity<IdentityUser>().HasIndex(u => u.NormalizedEmail).IsUnique();
        builder.Entity<IdentityRole>().ToTable("identity_roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("identity_user_roles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("identity_user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("identity_user_logins");
        builder.Entity<IdentityUserToken<string>>().ToTable("identity_user_tokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("identity_role_claims");
        builder.Entity<DataProtectionKey>().ToTable("identity_data_protection_keys");
        builder.Entity<RefreshSession>(session =>
        {
            session.ToTable("identity_refresh_sessions");
            session.Property(s => s.Id).HasMaxLength(36);
            session.Property(s => s.TokenId).HasMaxLength(36);
            // A deleted user takes their sessions along.
            session.HasOne<IdentityUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PendingRevocation>(pending =>
        {
            pending.ToTable("identity_pending_revocations");
            pending.HasKey(p => p.UserId);
            pending.HasOne<IdentityUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Invite>(invite =>
        {
            invite.ToTable("identity_invites");
            invite.Property(i => i.Id).HasMaxLength(36);
            invite.Property(i => i.Email).HasMaxLength(256);
            invite.Property(i => i.NormalizedEmail).HasMaxLength(256);
            invite.Property(i => i.CreatedBy).HasMaxLength(450);
            invite.Property(i => i.AcceptedUserId).HasMaxLength(450);
            invite.HasIndex(i => i.TokenHash).IsUnique();
            // Re-inviting revokes the older one under the account lock; the index makes "one open invite" hold regardless.
            invite.HasIndex(i => i.NormalizedEmail).IsUnique().HasFilter("\"AcceptedUtc\" IS NULL AND \"RevokedUtc\" IS NULL");
        });

        builder.Entity<OrgDepartment>(department =>
        {
            department.ToTable("identity_org_departments", t => t.HasCheckConstraint("CK_identity_org_departments_slug", $"\"Slug\" {SlugCheck}"));
            department.HasKey(d => d.Slug);
            department.Property(d => d.Slug).HasMaxLength(128);
            department.Property(d => d.Name).HasMaxLength(100);
            department.Property(d => d.CreatedBy).HasMaxLength(450);
        });
        builder.Entity<OrgTeam>(team =>
        {
            team.ToTable("identity_org_teams", t => t.HasCheckConstraint("CK_identity_org_teams_slug", $"\"Slug\" {SlugCheck}"));
            team.HasKey(t => t.Slug);
            team.Property(t => t.Slug).HasMaxLength(128);
            team.Property(t => t.Name).HasMaxLength(100);
            team.Property(t => t.CreatedBy).HasMaxLength(450);
            // Nothing is deleted in this slice; Restrict keeps a department from vanishing under its teams if that changes.
            team.HasOne<OrgDepartment>().WithMany().HasForeignKey(t => t.DepartmentSlug).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<OrgTeamMember>(member =>
        {
            member.ToTable("identity_org_team_members");
            member.HasKey(m => new { m.TeamSlug, m.UserId });
            member.Property(m => m.AddedBy).HasMaxLength(450);
            member.HasOne<OrgTeam>().WithMany().HasForeignKey(m => m.TeamSlug).OnDelete(DeleteBehavior.Restrict);
            // A deleted user leaves their teams; the membership lookup reads by user.
            member.HasOne<IdentityUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            member.HasIndex(m => m.UserId);
        });

        builder.Entity<EntraSettings>(settings =>
        {
            settings.ToTable("identity_entra_settings", t => t.HasCheckConstraint("CK_identity_entra_settings_single_row", $"\"Id\" = {Data.EntraSettings.SingletonId}"));
            settings.Property(s => s.Id).ValueGeneratedNever();
            settings.Property(s => s.TenantId).HasMaxLength(36);
            settings.Property(s => s.ClientId).HasMaxLength(36);
            settings.Property(s => s.UpdatedBy).HasMaxLength(450);
        });
        builder.Entity<EntraGroupMap>(group =>
        {
            group.ToTable("identity_entra_groups");
            group.HasKey(g => g.GroupId);
            group.Property(g => g.GroupId).HasMaxLength(36);
            group.Property(g => g.Label).HasMaxLength(100);
        });

        // Fixed ids and stamps: seed data must be identical on every model build.
        builder.Entity<IdentityRole>().HasData(SeededRoles.Select(role => new IdentityRole(role)
        {
            Id = role,
            NormalizedName = role.ToUpperInvariant(),
            ConcurrencyStamp = role
        }));
    }
}
