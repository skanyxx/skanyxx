using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// ASP.NET Core Identity's schema under <c>identity_*</c> table names, so it can share a database with other modules,
/// plus the Data Protection key ring (cookies and bearer tokens are protected with it, so every replica must share it
/// and it must survive a restart), the refresh-token chains and the invites.
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

        // Fixed ids and stamps: seed data must be identical on every model build.
        builder.Entity<IdentityRole>().HasData(SeededRoles.Select(role => new IdentityRole(role)
        {
            Id = role,
            NormalizedName = role.ToUpperInvariant(),
            ConcurrencyStamp = role
        }));
    }
}
